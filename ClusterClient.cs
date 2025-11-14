using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace ZVClusterApp.WinForms
{
    /// <summary>
    /// Lightweight TCP client wrapper for a DX cluster connection.
    /// Responsibilities:
    ///  - Establish and maintain a TCP connection (no Telnet option negotiation).
    ///  - Read incoming bytes, split into CR/LF terminated lines, raise <see cref="LineReceived"/>.
    ///  - Provide a CRLF line send method with automatic retry / quick reconnect semantics.
    ///  - Raise <see cref="Reconnected"/> when a full reconnect succeeds.
    ///  - Raise <see cref="Faulted"/> when an IO failure or remote close occurs so higher layers can react
    ///    (e.g. suppress keepalive pings until a successful reconnect).
    /// Thread-safety:
    ///  - Writes are serialized via <see cref="_writeLock"/>.
    ///  - Public send API is safe to call concurrently.
    ///  - Events are invoked on background threads; subscribers should marshal to UI if needed.
    /// </summary>
    public class ClusterClient : IDisposable
    {
        private readonly string _name;          // Logical cluster name
        private readonly string _host;          // Target host
        private readonly int _port;             // Target port
        private TcpClient? _tcp;                // Active TcpClient (null when disconnected)
        private NetworkStream? _stream;         // Cached NetworkStream (null when disconnected)
        private CancellationTokenSource? _cts;  // Lifetime token for read loop
        private readonly SemaphoreSlim _writeLock = new(1, 1); // Serialize writes / reconnect attempts

        /// <summary>Raised for each complete cluster text line (after CR/LF trimming).</summary>
        public event Action<string>? LineReceived;
        /// <summary>Raised after a successful full reconnect (new TCP socket established).</summary>
        public event Action? Reconnected;
        /// <summary>Raised when an IO failure occurs (read zero bytes, socket error, write unrecoverable) so manager can suppress keepalives.</summary>
        public event Action? Faulted;

        /// <summary>True if a socket and stream are currently established and appear connected.</summary>
        public bool IsConnected => _tcp != null && _tcp.Connected && _stream != null;

        /// <summary>Create a new cluster client for the given endpoint.</summary>
        public ClusterClient(string name, string host, int port)
        {
            _name = name; _host = host; _port = port;
        }

        /// <summary>
        /// Establish the TCP connection. Returns false if connect fails. Starts background read loop on success.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken token)
        {
            try
            {
                Debug.WriteLine($"[ClusterClient:{_name}] Connecting to {_host}:{_port}...");
                _tcp = new TcpClient();
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                await _tcp.ConnectAsync(_host, _port, connectCts.Token).ConfigureAwait(false);
                try { _tcp.NoDelay = true; } catch { }
                _stream = _tcp.GetStream();
                try { _stream.ReadTimeout = -1; _stream.WriteTimeout = 5000; } catch { }
                _cts = new CancellationTokenSource();
                // Fire-and-forget read loop
                _ = Task.Run(() => ReadLoopAsync(_cts.Token));
                Debug.WriteLine($"[ClusterClient:{_name}] Connected.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClusterClient:{_name}] Connect failed: {ex.GetType().Name}: {ex.Message}");
                DisposeConnection();
                // Signal fault to manager; next keepalive should be suppressed until reconnect attempt.
                RaiseFaultedSafe();
                return false;
            }
        }

        /// <summary>
        /// Background loop: reads bytes, converts to ASCII, accumulates into a buffer, extracts CR/LF terminated lines.
        /// On IO failure or remote close signals <see cref="Faulted"/>.
        /// </summary>
        private async Task ReadLoopAsync(CancellationToken token)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();
            bool ioFaulted = false; // Tracks whether loop ended due to IO problem (vs cancellation)
            try
            {
                while (!token.IsCancellationRequested && _stream != null)
                {
                    // ReadAsync returns 0 when remote closed its side.
                    int n = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    if (n == 0)
                    {
                        Debug.WriteLine($"[ClusterClient:{_name}] Remote closed (Read returned 0).");
                        ioFaulted = true; // Treat remote close as a fault for keepalive suppression.
                        break;
                    }

                    // Convert bytes to ASCII string (cluster servers are typically ASCII/latin1).
                    var chunk = Encoding.ASCII.GetString(buffer, 0, n);
                    Debug.WriteLine($"[ClusterClient:{_name}] RX {n} bytes: '{EscapeVisible(Preview(chunk))}'");
                    sb.Append(chunk);

                    // Attempt to extract all complete lines currently buffered.
                    string? line;
                    while ((line = ReadLineFromBuffer(sb)) != null)
                    {
                        Debug.WriteLine($"[ClusterClient:{_name}] RX line CRLF: '{EscapeVisible(line)}'");
                        LineReceived?.Invoke(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path - not a fault.
            }
            catch (Exception ex)
            {
                // Any unexpected exception in read is treated as fault.
                ioFaulted = true;
                Debug.WriteLine($"[ClusterClient:{_name}] ReadLoop exception: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // Flush any remaining non-terminated data as one last line (avoid losing partial last line).
                try
                {
                    if (sb.Length > 0)
                    {
                        var rem = sb.ToString().TrimEnd('\r', '\n');
                        if (!string.IsNullOrWhiteSpace(rem))
                        {
                            Debug.WriteLine($"[ClusterClient:{_name}] RX flush remaining: '{EscapeVisible(Preview(rem))}'");
                            LineReceived?.Invoke(rem);
                        }
                    }
                }
                catch { }

                Debug.WriteLine($"[ClusterClient:{_name}] ReadLoop ending; disposing connection.");
                DisposeConnection();

                // Notify manager only if ended due to remote close / IO failure.
                if (ioFaulted) RaiseFaultedSafe();
            }
        }

        /// <summary>
        /// Send one logical line (appends CRLF). Performs quick liveness check and one reconnect attempt on failure.
        /// Raises <see cref="Faulted"/> if unable to restore connectivity.
        /// </summary>
        public async Task SendRawLineAsync(string line)
        {
            // Quick liveness probe: if stream not usable attempt fast reconnect before sending.
            if (!IsStreamUsable())
            {
                Debug.WriteLine($"[ClusterClient:{_name}] Stream not usable; attempting quick reconnect before send...");
                var ok = await TryReconnectAsync(4000).ConfigureAwait(false);
                if (!ok)
                {
                    RaiseFaultedSafe();
                    throw new InvalidOperationException("Not connected");
                }
            }

            // Refresh stream reference if needed.
            _stream ??= _tcp?.Connected == true ? _tcp.GetStream() : null;
            var stream = _stream;
            if (stream == null)
            {
                RaiseFaultedSafe();
                throw new InvalidOperationException("Not connected");
            }

            var text = line ?? string.Empty;
            var bytes = Encoding.ASCII.GetBytes(text + "\r\n");
            Debug.WriteLine($"[ClusterClient:{_name}] TX ({bytes.Length} bytes) line=\"{EscapeVisible(text)}\\r\\n\"");

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    await TryWriteAsync(stream, bytes).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ClusterClient:{_name}] Write path threw: {ex.GetType().Name}: {ex.Message}. Attempting reconnect and resend once...");
                    // Single automatic reconnect attempt.
                    if (await TryReconnectAsync(4000).ConfigureAwait(false))
                    {
                        var s2 = _stream;
                        if (s2 != null)
                        {
                            await TryWriteAsync(s2, bytes).ConfigureAwait(false);
                            return; // Success after reconnect
                        }
                    }
                    // Unrecoverable write failure - signal fault and rethrow.
                    RaiseFaultedSafe();
                    throw;
                }
            }
            finally { _writeLock.Release(); }
        }

        /// <summary>
        /// Core write routine with fallback strategies: NetworkStream write, refreshed stream retry, raw Socket.Send.
        /// On final failure raises <see cref="Faulted"/> before rethrowing.
        /// </summary>
        private async Task TryWriteAsync(NetworkStream stream, byte[] bytes)
        {
            try
            {
                Debug.WriteLine($"[ClusterClient:{_name}] WriteAsync {bytes.Length} bytes to NetworkStream");
                await stream.WriteAsync(bytes.AsMemory(0, bytes.Length)).ConfigureAwait(false);
                try { await stream.FlushAsync().ConfigureAwait(false); } catch { }
                Debug.WriteLine($"[ClusterClient:{_name}] WriteAsync OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClusterClient:{_name}] WriteAsync failed: {ex.GetType().Name}: {ex.Message}. Trying to refresh stream...");
                // Attempt refreshed stream retry
                try
                {
                    if (_tcp?.Connected == true)
                    {
                        _stream = _tcp.GetStream();
                        stream = _stream;
                        if (stream != null)
                        {
                            Debug.WriteLine($"[ClusterClient:{_name}] Retry write on refreshed NetworkStream");
                            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length)).ConfigureAwait(false);
                            try { await stream.FlushAsync().ConfigureAwait(false); } catch { }
                            Debug.WriteLine($"[ClusterClient:{_name}] Retry write OK");
                            return;
                        }
                    }
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine($"[ClusterClient:{_name}] Retry on refreshed stream failed: {ex2.GetType().Name}: {ex2.Message}");
                }

                // Fallback to raw Socket.Send if still connected.
                try
                {
                    var sock = _tcp?.Client;
                    if (sock != null && sock.Connected)
                    {
                        Debug.WriteLine($"[ClusterClient:{_name}] Fallback Socket.Send {bytes.Length} bytes");
                        sock.Send(bytes, SocketFlags.None);
                        Debug.WriteLine($"[ClusterClient:{_name}] Fallback Socket.Send OK");
                        return;
                    }
                }
                catch (Exception ex3)
                {
                    Debug.WriteLine($"[ClusterClient:{_name}] Fallback Socket.Send failed: {ex3.GetType().Name}: {ex3.Message}");
                }

                Debug.WriteLine($"[ClusterClient:{_name}] Write failed - rethrowing");
                RaiseFaultedSafe(); // Final failure -> mark fault
                throw;
            }
        }

        /// <summary>
        /// Determines whether the current stream/socket appears usable (no immediate EOF indication).
        /// </summary>
        private bool IsStreamUsable()
        {
            try
            {
                if (_tcp == null || _stream == null) return false;
                var sock = _tcp.Client;
                if (sock == null) return false;
                bool readable = sock.Poll(0, SelectMode.SelectRead);
                if (readable && sock.Available == 0) return false; // Remote closed
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Attempts a fast reconnect within the specified timeout. On success raises <see cref="Reconnected"/>.
        /// Failure does NOT raise Faulted here (callers decide), except initial connect failure already does.
        /// </summary>
        private async Task<bool> TryReconnectAsync(int timeoutMs)
        {
            try
            {
                Debug.WriteLine($"[ClusterClient:{_name}] TryReconnectAsync starting...");
                DisposeConnection(); // Tear down previous resources first.
                using var cts = new CancellationTokenSource(timeoutMs);
                var ok = await ConnectAsync(cts.Token).ConfigureAwait(false);
                Debug.WriteLine($"[ClusterClient:{_name}] TryReconnectAsync result: {(ok ? "OK" : "FAIL")}");
                if (ok)
                {
                    // Fire Reconnected asynchronously to avoid blocking send path.
                    var handler = Reconnected;
                    if (handler != null)
                    {
                        try { _ = Task.Run(() => { try { handler(); } catch { } }); } catch { }
                    }
                }
                return ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClusterClient:{_name}] TryReconnectAsync exception: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Raise Faulted event safely (catch subscriber exceptions).</summary>
        private void RaiseFaultedSafe()
        {
            try { Faulted?.Invoke(); } catch { }
        }

        /// <summary>Escape control characters for debug visibility.</summary>
        private static string EscapeVisible(string s)
        {
            if (s == null) return string.Empty;
            var r = s.Replace("\\", "\\\\");
            r = r.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0");
            return r;
        }

        /// <summary>Return a short preview of a long string for logging.</summary>
        private static string Preview(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            const int max = 256;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>
        /// Extract one CR or CRLF terminated line from the buffer (removes it from buffer). Returns null if incomplete.
        /// </summary>
        private static string? ReadLineFromBuffer(StringBuilder sb)
        {
            for (int i = 0; i < sb.Length; i++)
            {
                char ch = sb[i];
                if (ch == '\n' || ch == '\r')
                {
                    int lineLen = i;        // characters prior to newline
                    int removeLen = 1;      // number of chars to remove including terminator(s)
                    if (ch == '\r' && i + 1 < sb.Length && sb[i + 1] == '\n') removeLen = 2; // handle CRLF
                    var line = sb.ToString(0, lineLen);
                    sb.Remove(0, lineLen + removeLen);
                    return line.TrimEnd('\r');
                }
            }
            return null; // no complete line yet
        }

        /// <summary>
        /// Public disconnect request: cancels read loop and disposes underlying connection resources.
        /// </summary>
        public void Disconnect()
        {
            Debug.WriteLine($"[ClusterClient:{_name}] Disconnect requested.");
            _cts?.Cancel();
            DisposeConnection();
        }

        /// <summary>
        /// Dispose underlying stream / socket resources and reset connection state fields.
        /// </summary>
        private void DisposeConnection()
        {
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Close(); } catch { }
            _stream = null; _tcp = null; _cts = null;
            Debug.WriteLine($"[ClusterClient:{_name}] Disposed connection.");
        }

        /// <summary>Implements <see cref="IDisposable"/> by calling <see cref="Disconnect"/>.</summary>
        public void Dispose() => Disconnect();
    }
}
