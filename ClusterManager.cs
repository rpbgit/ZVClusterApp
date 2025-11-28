using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics; // ADDED
using System.Text.RegularExpressions; // ADDED for prompt detection

namespace ZVClusterApp.WinForms
{
    /// <summary>
    /// Manages multiple <see cref="ClusterClient"/> instances, tracks which cluster is currently active,
    /// dispatches received lines, performs auto-login sequences, and runs a keepalive loop.
    ///
    /// Keepalive behavior:
    ///  - Keepalive pings are suppressed for a cluster after any IO fault until a successful reconnect.
    ///  - A proactive reconnection worker is started when a fault is detected so recovery happens even without user input.
    /// </summary>
    public class ClusterManager : IDisposable
    {
        private readonly AppSettings _settings;                           // Application settings source
        private readonly Dictionary<string, ClusterClient> _clients = new(); // All cluster client instances keyed by name
        private ClusterClient? _activeClient;                             // Currently active (connected) cluster client instance

        /// <summary>Raised when a line is received from any cluster (cluster name, line text).</summary>
        public event Action<string, string>? LineReceived;
        /// <summary>Name of the currently active cluster or null when none connected.</summary>
        public string? ActiveClusterName { get; private set; }

        // Activity / keepalive tracking ----------------------------------------------------------
        private readonly object _activityLock = new();                    // Protects access to dictionaries / suppression set / reconnection trackers
        private readonly Dictionary<string, DateTime> _lastActivityUtc = new(); // Last time we saw *any* line from each cluster
        private readonly Dictionary<string, DateTime> _lastKeepAliveUtc = new(); // Last time we sent a keepalive ping per cluster
        private readonly HashSet<string> _keepAliveSuppressed = new(StringComparer.OrdinalIgnoreCase); // Clusters currently suppressing keepalive pings due to IO fault
        private CancellationTokenSource _keepAliveCts = new();             // Lifetime token source for keepalive loop
        private Task? _keepAliveTask;                                     // Keepalive loop task

        // Proactive reconnection management ------------------------------------------------------
        // One reconnect worker per cluster name at most. We store the task and a CTS to cancel it when no longer needed.
        private readonly Dictionary<string, (Task task, CancellationTokenSource cts)> _reconnectWorkers = new(StringComparer.OrdinalIgnoreCase);

        // NEW: prevent overlapping login replays per cluster
        private readonly HashSet<string> _loginReplayInProgress = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Create the manager and initialize clients based on settings.</summary>
        public ClusterManager(AppSettings settings)
        {
            _settings = settings;
            foreach (var c in _settings.Clusters)
            {
                // NOTE: Port forced to 7300 per original logic; settings port ignored.
                int port = 7300;
                var client = new ClusterClient(c.Name, c.Host, port);
                _clients[c.Name] = client;
                _lastActivityUtc[c.Name] = DateTime.UtcNow; // seed activity baseline

                // Hook line reception -> update activity + bubble up externally.
                client.LineReceived += line =>
                {
                    UpdateLastActivity(c.Name);
                    LineReceived?.Invoke(c.Name, line);
                };

                // On reconnect: clear keepalive suppression and perform auto-login sequence if active.
                client.Reconnected += () =>
                {
                    UnsuppressKeepAlive(c.Name);
                    StopReconnectWorker(c.Name); // stop any ongoing proactive worker for this cluster
                    OnClientReconnected(c.Name);
                };

                // On fault: suppress keepalive pings until next successful reconnect and start proactive reconnection loop.
                client.Faulted += () =>
                {
                    SuppressKeepAlive(c.Name);
                    StartReconnectWorker(c.Name);
                };
            }
            StartKeepAliveLoop();
        }

        // ---------------------------------------------------------------------------------------
        // Keepalive suppression helpers
        private void SuppressKeepAlive(string name)
        {
            lock (_activityLock)
            {
                _keepAliveSuppressed.Add(name);
            }
        }
        private void UnsuppressKeepAlive(string name)
        {
            lock (_activityLock)
            {
                if (_keepAliveSuppressed.Remove(name))
                {
                    // Reset keepalive timestamp so we don't instantly send a ping on recovery.
                    _lastKeepAliveUtc[name] = DateTime.UtcNow;
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Proactive reconnection helpers
        /// <summary>
        /// Start a background reconnection worker for a given cluster if not already running.
        /// The worker attempts reconnects with exponential backoff and jitter until one of the following happens:
        ///  - Successful reconnect (the client's Reconnected event will stop the worker).
        ///  - The cluster is no longer the active cluster.
        ///  - The manager or this worker is canceled (Disconnect or Dispose).
        /// </summary>
        private void StartReconnectWorker(string name)
        {
            lock (_activityLock)
            {
                // Only one worker per cluster.
                if (_reconnectWorkers.ContainsKey(name)) return;

                // Only attempt to proactively reconnect the currently active cluster (others are idle until chosen).
                if (!string.Equals(ActiveClusterName, name, StringComparison.OrdinalIgnoreCase)) return;

                if (!_clients.TryGetValue(name, out var client)) return;

                var cts = new CancellationTokenSource();
                var token = cts.Token;
                var task = Task.Run(async () =>
                {
                    // Basic backoff parameters.
                    var rand = new Random();
                    int delayMs = 1500;          // initial backoff
                    const int maxDelayMs = 60_000; // cap

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // If cluster changed while we were waiting, stop trying.
                            if (!string.Equals(ActiveClusterName, name, StringComparison.OrdinalIgnoreCase)) break;

                            // Already connected? nothing to do.
                            if (client.IsConnected) break;

                            // Attempt a quick reconnect with its own timeout (5s).
                            using var timeout = new CancellationTokenSource(5000);
                            var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, token);
                            var ok = await client.ConnectAsync(linked.Token).ConfigureAwait(false);
                            if (ok)
                            {
                                // Success: the client will raise Reconnected, which stops this worker.
                                break;
                            }
                        }
                        catch { }

                        // Compute next backoff: exponential growth with 10-20% jitter.
                        int jitter = (int)(delayMs * (0.1 + rand.NextDouble() * 0.1)); // 10-20%
                        int wait = Math.Min(maxDelayMs, delayMs + jitter);
                        try { await Task.Delay(wait, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                        delayMs = Math.Min(maxDelayMs, delayMs * 2);
                    }
                }, token);

                _reconnectWorkers[name] = (task, cts);
            }
        }

        /// <summary>
        /// Stop and remove a reconnection worker for a given cluster if present.
        /// </summary>
        private void StopReconnectWorker(string name)
        {
            lock (_activityLock)
            {
                if (_reconnectWorkers.TryGetValue(name, out var t))
                {
                    try { t.cts.Cancel(); } catch { }
                    _reconnectWorkers.Remove(name);
                }
            }
        }

        /// <summary>
        /// Stop and remove all reconnection workers (used on Disconnect-all and Dispose).
        /// </summary>
        private void StopAllReconnectWorkers()
        {
            lock (_activityLock)
            {
                foreach (var kvp in _reconnectWorkers)
                {
                    try { kvp.Value.cts.Cancel(); } catch { }
                }
                _reconnectWorkers.Clear();
            }
        }

        // ADD: Wait specifically for common login prompts with timeout (non-blocking)
        private async Task WaitForLoginPromptAsync(string name, TimeSpan timeout, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Local handler subscribes to bubbled lines (LineReceived is raised via client.LineReceived hook)
            void OnAnyLine(string cluster, string line)
            {
                if (!string.Equals(cluster, name, StringComparison.OrdinalIgnoreCase)) return;
                try
                {
                    if (line == null) return;
                    if (Regex.IsMatch(line, @"\b(login|call|username)\s*:", RegexOptions.IgnoreCase))
                        tcs.TrySetResult(true);
                }
                catch { }
            }

            LineReceived += OnAnyLine;
            try
            {
                var delayTask = Task.Delay(timeout, token);
                var winner = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);
                // ignore result; we just timeout/fallback if no prompt
            }
            catch { }
            finally
            {
                try { LineReceived -= OnAnyLine; } catch { }
            }
        }

        // HELPER: unified login + default command replay
        private async Task ReplayLoginAndDefaultsAsync(string name, ClusterClient client, ClusterDefinition def, CancellationToken token, bool forceLogin)
        {
            // Prevent overlapping replays per cluster
            lock (_activityLock)
            {
                if (_loginReplayInProgress.Contains(name)) return;
                _loginReplayInProgress.Add(name);
            }

            try
            {
                // Wait for initial prompt or some activity (up to 3s)
                var waitDeadline = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < waitDeadline)
                {
                    bool hadActivity;
                    lock (_activityLock)
                        hadActivity = _lastActivityUtc.TryGetValue(name, out var last) && (DateTime.UtcNow - last) < TimeSpan.FromSeconds(2);
                    if (hadActivity) break;
                    await Task.Delay(200, token).ConfigureAwait(false);
                }

                var myCall = _settings.MyCall?.Trim();
                bool doLogin = (def.AutoLogin || forceLogin) && !string.IsNullOrEmpty(myCall);

                if (_settings.DebugLogEnabled)
                    Debug.WriteLine($"[ClusterManager] Replay start cluster={name} doLogin={doLogin} defaults={(def.DefaultCommands?.Length ?? 0)}");

                if (doLogin)
                {
                    // Prefer waiting for explicit prompt if it appears, fallback to immediate send after timeout
                    try { await WaitForLoginPromptAsync(name, TimeSpan.FromSeconds(3), token).ConfigureAwait(false); } catch { }

                    LineReceived?.Invoke(name, "[CMD] " + myCall);
                    try { await client.SendRawLineAsync(myCall!).ConfigureAwait(false); } catch { }
                    await Task.Delay(150, token).ConfigureAwait(false);
                }

                if (def.DefaultCommands != null && def.DefaultCommands.Length > 0 && (def.AutoLogin || forceLogin))
                {
                    foreach (var raw in def.DefaultCommands)
                    {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var hashIdx = raw.IndexOf('#');
                        var core = hashIdx >= 0 ? raw.Substring(0, hashIdx) : raw;
                        core = core.Trim();
                        if (core.Length == 0) continue;

                        LineReceived?.Invoke(name, "[CMD] " + core);
                        try { await client.SendRawLineAsync(core).ConfigureAwait(false); } catch { }
                        await Task.Delay(180, token).ConfigureAwait(false);
                    }
                }

                try { await client.SendRawLineAsync(string.Empty).ConfigureAwait(false); } catch { }

                if (_settings.DebugLogEnabled)
                    Debug.WriteLine($"[ClusterManager] Replay complete cluster={name}");
            }
            catch (OperationCanceledException)
            {
                if (_settings.DebugLogEnabled)
                    Debug.WriteLine($"[ClusterManager] Replay canceled cluster={name}");
            }
            catch (Exception ex)
            {
                if (_settings.DebugLogEnabled)
                    Debug.WriteLine($"[ClusterManager] Replay error cluster={name}: {ex.Message}");
            }
            finally
            {
                lock (_activityLock) _loginReplayInProgress.Remove(name);
            }
        }

        /// <summary>
        /// Performs re-login / command replay after a successful reconnect (only if cluster remains active).
        /// </summary>
        private async void OnClientReconnected(string name)
        {
            try
            {
                // Ensure the reconnecting cluster becomes active before replay
                if (_clients.TryGetValue(name, out var client) && client != null)
                {
                    ActiveClusterName = name;
                    _activeClient = client;
                }

                var def = GetDefinition(name);
                if (def == null || _activeClient == null) return;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await ReplayLoginAndDefaultsAsync(name, _activeClient, def, timeoutCts.Token, forceLogin: true).ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>Enumerates cluster names known to the manager.</summary>
        public IEnumerable<string> ClusterNames => _clients.Keys;

        /// <summary>Returns the <see cref="ClusterDefinition"/> for a given name or null if not found.</summary>
        public ClusterDefinition? GetDefinition(string name) => _settings.Clusters.FirstOrDefault(x => x.Name == name);

        /// <summary>
        /// Establish connection to specified cluster. On success marks it active and optionally performs auto-login sequence.
        /// Keepalive suppression is cleared on successful connect.
        /// </summary>
        public async Task<bool> ConnectAsync(string name, CancellationToken token, bool forceLogin = false)
        {
            if (!_clients.TryGetValue(name, out var client)) return false;
            var def = GetDefinition(name);
            var ok = await client.ConnectAsync(token).ConfigureAwait(false);
            if (ok)
            {
                ActiveClusterName = name;
                _activeClient = client;
                UpdateLastActivity(name);
                UnsuppressKeepAlive(name);
                StopReconnectWorker(name);

                if (def != null)
                {
                    await ReplayLoginAndDefaultsAsync(name, client, def, token, forceLogin).ConfigureAwait(false);
                }
            }
            return ok;
        }

        /// <summary>Send a raw line through the currently active client (echoed to UI first).</summary>
        public Task SendRawAsync(string line)
        {
            if (_activeClient == null) return Task.CompletedTask;
            var activeName = ActiveClusterName ?? string.Empty;
            LineReceived?.Invoke(activeName, "[CMD] " + (line ?? string.Empty));
            return _activeClient.SendRawLineAsync(line);
        }

        /// <summary>
        /// Disconnect one cluster or all clusters if name is null. Clears active cluster if it matches the disconnected one.
        /// Also stops any proactive reconnect worker tied to that cluster.
        /// </summary>
        public void Disconnect(string? name = null)
        {
            if (name == null)
            {
                foreach (var c in _clients.Values)
                {
                    try { c.Disconnect(); } catch { }
                }
                StopAllReconnectWorkers();
                ActiveClusterName = null; _activeClient = null;
            }
            else if (_clients.TryGetValue(name, out var c))
            {
                c.Disconnect();
                StopReconnectWorker(name);
                if (ActiveClusterName == name) { ActiveClusterName = null; _activeClient = null; }
            }
        }

        /// <summary>Dispose manager: stop keepalive loop, stop reconnect workers and dispose all clients.</summary>
        public void Dispose()
        {
            try { _keepAliveCts.Cancel(); } catch { }
            try { _keepAliveTask?.Wait(250); } catch { }
            _keepAliveCts.Dispose();
            StopAllReconnectWorkers();
            foreach (var c in _clients.Values) c.Dispose();
            _clients.Clear();
        }

        /// <summary>Update last activity time for given cluster (thread-safe).</summary>
        private void UpdateLastActivity(string name)
        {
            lock (_activityLock)
            {
                _lastActivityUtc[name] = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Starts a background loop which periodically (every 30s) decides whether to send a keepalive ping.
        /// Conditions for keepalive:
        ///  - An active cluster is selected.
        ///  - Keepalive not suppressed for that cluster.
        ///  - Both: (Now - LastActivity) AND (Now - LastKeepAlive) exceed inactivity threshold (3 minutes).
        ///  - Ping send success updates last keepalive timestamp; failure triggers suppression.
        /// </summary>
        private void StartKeepAliveLoop()
        {
            _keepAliveTask = Task.Run(async () =>
            {
                var token = _keepAliveCts.Token;
                var inactivity = TimeSpan.FromMinutes(3);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                        var now = DateTime.UtcNow;
                        var activeName = ActiveClusterName;
                        var client = _activeClient;
                        if (string.IsNullOrEmpty(activeName) || client == null)
                            continue; // nothing active -> skip

                        bool suppressed;
                        DateTime lastAct;
                        lock (_activityLock)
                        {
                            suppressed = _keepAliveSuppressed.Contains(activeName);
                            lastAct = _lastActivityUtc.TryGetValue(activeName, out var dt) ? dt : now;
                        }
                        if (suppressed)
                            continue; // currently faulted -> wait for reconnect

                        var lastKeep = _lastKeepAliveUtc.TryGetValue(activeName, out var lk) ? lk : DateTime.MinValue;

                        // Inactivity threshold reached for both last activity and last keepalive -> send ping.
                        if (now - lastAct >= inactivity && now - lastKeep >= inactivity)
                        {
                            try
                            {
                                await client.SendRawLineAsync(" ").ConfigureAwait(false); // space keeps connection alive
                                _lastKeepAliveUtc[activeName] = now; // record successful keepalive
                            }
                            catch
                            {
                                // Any failure sending keepalive -> suppress further pings until reconnect and start proactive reconnection.
                                SuppressKeepAlive(activeName);
                                StartReconnectWorker(activeName);
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { /* swallow transient errors */ }
                }
            });
        }
    }
}
