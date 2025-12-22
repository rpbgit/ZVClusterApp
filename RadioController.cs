using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ZVClusterApp.WinForms
{
    public enum RigType { Unknown, Icom, Yaesu, Kenwood }

    public class RadioController : IDisposable
    {
        // The underlying driver (Yaesu/Icom/Kenwood) that actually talks to SerialPort.
        // IMPORTANT: all access to this object must be serialized to a single thread
        // to avoid SerialPort concurrency problems.
        private IRadioDriver _driver;

        private RigType _rig;

        // Cached model selection; we keep this so we can re-apply if we swap drivers.
        private string _modelId = "IC-7300"; // default model selection

        // ------------------------------------------------------------------------------------
        // Background CAT worker
        //
        // Goal: run ALL CAT commands (and any driver property changes that might cause reconnect)
        // on a single background thread.
        //
        // This means:
        //  - the pacing sleep (Thread.Sleep in SerialRadioDriverBase) blocks ONLY the worker thread
        //  - UI remains responsive
        //  - serial operations are ordered and never overlap
        // ------------------------------------------------------------------------------------

        // Unbounded queue of "work items" for the CAT worker to execute.
        // SingleReader=true => exactly one worker thread reads items and executes them sequentially.
        private readonly Channel<CatWorkItem> _catQueue = Channel.CreateUnbounded<CatWorkItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        // Cancels the worker loop on shutdown.
        private readonly CancellationTokenSource _catCts = new();

        // The worker task itself.
        private readonly Task _catWorker;

        public RadioController(string port, int baud, bool enabled)
        {
            _rig = RigType.Icom; // default
            _driver = CreateDriver(_rig, port, baud, enabled);

            Debug.WriteLine($"[Radio] Controller created. Rig={_rig}, Enabled={enabled}, Port={port}, Baud={baud}");

            // Start the single background worker that processes CAT work items.
            _catWorker = Task.Run(() => CatWorkerLoopAsync(_catCts.Token));
        }

        // ------------------------------------------------------------------------------------
        // Properties: READS return current state immediately.
        // WRITES are queued onto the CAT worker to avoid racing with SerialPort operations.
        // ------------------------------------------------------------------------------------

        // Rig selection
        public RigType Rig
        {
            get => _rig;
            set
            {
                if (_rig == value) return;

                // Queue the rig switch so it can't overlap with in-flight serial writes.
                _ = EnqueueAsync<bool>(() =>
                {
                    Debug.WriteLine($"[Radio] Rig changing: {_rig} -> {value}");

                    var old = _driver;
                    _driver = CreateDriver(value, old.Port, old.Baud, old.Enabled);

                    // Preserve model selection across driver switches.
                    try { _driver.ModelId = _modelId; } catch { }

                    // Dispose old driver/port resources on the worker thread.
                    try { old.Dispose(); } catch { }

                    _rig = value;
                    return true;
                });
            }
        }

        // Icom-only property passthrough
        public byte IcomAddress
        {
            get => (_driver as IcomCivDriver)?.IcomAddress ?? 0x94;
            set
            {
                _ = EnqueueAsync<bool>(() =>
                {
                    if (_driver is IcomCivDriver ic)
                        ic.IcomAddress = value;

                    return true;
                });
            }
        }

        // Dynamic properties
        public bool Enabled
        {
            get => _driver.Enabled;
            set
            {
                _ = EnqueueAsync<bool>(() =>
                {
                    _driver.Enabled = value;
                    return true;
                });
            }
        }

        public string Port
        {
            get => _driver.Port;
            set
            {
                _ = EnqueueAsync<bool>(() =>
                {
                    _driver.Port = value;
                    return true;
                });
            }
        }

        public int Baud
        {
            get => _driver.Baud;
            set
            {
                _ = EnqueueAsync<bool>(() =>
                {
                    _driver.Baud = value;
                    return true;
                });
            }
        }

        // StopBits passthrough as 1 or 2 (maps to enum inside driver)
        public int StopBits
        {
            get
            {
                if (_driver is SerialRadioDriverBase srb) return srb.StopBits;
                return 1;
            }
            set
            {
                _ = EnqueueAsync<bool>(() =>
                {
                    if (_driver is SerialRadioDriverBase srb)
                        srb.StopBits = (value == 2) ? 2 : 1;
                    return true;
                });
            }
        }

        // Selected model ID facade for settings dialog
        public string ModelId
        {
            get => _modelId;
            set
            {
                _modelId = value ?? _modelId;
                _ = EnqueueAsync<bool>(() =>
                {
                    try { _driver.ModelId = _modelId; } catch { }
                    return true;
                });
            }
        }

        // ------------------------------------------------------------------------------------
        // Public CAT operations
        // ------------------------------------------------------------------------------------

        // NOTE: This sync method will block the caller while awaiting the worker result.
        // If caller is UI thread, it will still "freeze" until completion.
        // Prefer SendFrequencyAsync from UI to keep UI responsive.
        public bool SendFrequency(int frequencyHz, string? mode = null)
            => SendFrequencyAsync(frequencyHz, mode).GetAwaiter().GetResult();

        // Async version: UI should call this with await.
        public Task<bool> SendFrequencyAsync(int frequencyHz, string? mode = null)
        {
            return EnqueueAsync(() =>
            {
                Debug.WriteLine($"[Radio] SendFrequency facade: {frequencyHz} Hz, mode='{mode}' via {_rig}");
                return _driver.SetFrequencyAndMode(frequencyHz, mode);
            });
        }

        public bool Connect()
            => ConnectAsync().GetAwaiter().GetResult();

        public Task<bool> ConnectAsync()
        {
            return EnqueueAsync(() => _driver.Connect());
        }

        public void Disconnect()
        {
            // Fire-and-forget on worker.
            _ = EnqueueAsync<bool>(() =>
            {
                _driver.Disconnect();
                return true;
            });
        }

        // Apply settings (includes pacing delay via SerialRadioDriverBase.ApplySettings)
        public void ApplySettings(AppSettings s)
        {
            // Queue everything so settings changes can't overlap with CAT commands.
            _ = EnqueueAsync<bool>(() =>
            {
                // Apply settings to current driver (includes pacing delay)
                if (_driver is SerialRadioDriverBase serial)
                    serial.ApplySettings(s);
                else
                {
                    // Fallback
                    _driver.Enabled = s.CatEnabled;
                    _driver.Port = s.CatPort;
                    _driver.Baud = s.CatBaud;
                    if (_driver is SerialRadioDriverBase srb)
                        srb.StopBits = (s.CatStopBits == System.IO.Ports.StopBits.Two) ? 2 : 1;
                }

                // Ensure rig driver matches settings (switch driver if needed).
                if (_rig != s.Rig)
                {
                    Debug.WriteLine($"[Radio] Rig changing (ApplySettings): {_rig} -> {s.Rig}");

                    var old = _driver;
                    _driver = CreateDriver(s.Rig, old.Port, old.Baud, old.Enabled);

                    try { old.Dispose(); } catch { }

                    _rig = s.Rig;
                }

                // Apply model selection (driver may change its internal CAT profile).
                _modelId = s.CatModelId ?? _modelId;
                try { _driver.ModelId = _modelId; } catch { }

                // Apply Icom CI-V address if applicable.
                if (_driver is IcomCivDriver ic)
                    ic.IcomAddress = s.IcomAddress;

                return true;
            });
        }

        // ------------------------------------------------------------------------------------
        // Driver factory
        // ------------------------------------------------------------------------------------

        private static IRadioDriver CreateDriver(RigType rig, string port, int baud, bool enabled)
        {
            SerialRadioDriverBase drv = rig switch
            {
                RigType.Icom => new IcomCivDriver(),
                RigType.Kenwood => new KenwoodCatDriver(),
                RigType.Yaesu => new YaesuCatDriver(),
                _ => new IcomCivDriver(),
            };

            // Initial settings for new driver instance.
            // (These may trigger Disconnect() logic inside the driver; ok during creation.)
            drv.Port = port;
            drv.Baud = baud;
            drv.Enabled = enabled;

            return drv;
        }

        // ------------------------------------------------------------------------------------
        // Worker internals
        // ------------------------------------------------------------------------------------

        // Represents one item of work for the CAT worker to execute.
        private sealed class CatWorkItem
        {
            public required Func<object?> Work { get; init; }
            public required TaskCompletionSource<object?> Tcs { get; init; }
        }

        // Enqueue a piece of work and return a Task representing the result.
        // Work will run on the single CAT worker thread, sequentially.
        private Task<T> EnqueueAsync<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // If the writer is completed, enqueuing fails.
            if (!_catQueue.Writer.TryWrite(new CatWorkItem
            {
                Work = () => work(),
                Tcs = tcs
            }))
            {
                tcs.TrySetException(new InvalidOperationException("CAT queue is not accepting work."));
            }

            // Project the object result to T.
            return tcs.Task.ContinueWith(
                t => (T)t.Result!,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        // The single background loop that serializes all CAT work.
        private async Task CatWorkerLoopAsync(CancellationToken token)
        {
            try
            {
                while (await _catQueue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (_catQueue.Reader.TryRead(out var item))
                    {
                        try
                        {
                            var result = item.Work();
                            item.Tcs.TrySetResult(result);
                        }
                        catch (Exception ex)
                        {
                            item.Tcs.TrySetException(ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Radio] CAT worker crashed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            // Stop worker first (prevents further writes and stops processing).
            try { _catCts.Cancel(); } catch { }
            try { _catQueue.Writer.TryComplete(); } catch { }

            // Give worker a chance to exit.
            try { _catWorker.Wait(500); } catch { }

            // Dispose driver and token source.
            try { _driver.Dispose(); } catch { }
            try { _catCts.Dispose(); } catch { }
        }
    }
}
