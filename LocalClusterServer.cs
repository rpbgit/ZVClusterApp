using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZVClusterApp.WinForms
{
    public sealed class LocalClusterServer : IDisposable
    {
        private readonly int _port;
        private TcpListener? _listener;
        private readonly List<Client> _clients = new();
        private readonly object _gate = new();
        private CancellationTokenSource? _cts;
        private Task? _acceptTask;

        // Raised when a connected client sends a line; handler can forward to the upstream cluster
        public event Func<string, Task>? CommandReceived;
        // Raised whenever the number of connected clients changes; parameter is the new count
        public event Action<int>? ClientCountChanged;

        public LocalClusterServer(int port = 7373) => _port = port;

        // Expose current connected client count
        public int ClientCount
        {
            get { lock (_gate) return _clients.Count; }
        }

        public void Start()
        {
            if (_listener != null) return;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            // Close clients
            List<Client> toClose;
            lock (_gate) toClose = _clients.ToList();
            foreach (var c in toClose) { try { c.Dispose(); } catch { } }
            lock (_gate) _clients.Clear();
            try { _acceptTask?.Wait(250); } catch { }
            _acceptTask = null;
            _cts?.Dispose(); _cts = null;

            // Notify count changed after clearing
            SafeRaiseClientCountChanged();
        }

        public void BroadcastLine(string line)
        {
            if (line == null) return;
            List<Client> snapshot;
            lock (_gate) snapshot = _clients.ToList();
            foreach (var c in snapshot)
            {
                try { c.WriteLine(line); }
                catch { try { RemoveClient(c); } catch { } }
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _listener != null)
                {
                    TcpClient tcp;
                    try { tcp = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    catch { continue; }

                    var client = new Client(tcp, RemoveClient);
                    lock (_gate) _clients.Add(client);
                    SafeRaiseClientCountChanged();

                    try { client.WriteLine("Welcome to ZV Cluster local server"); client.WriteLine("Type commands and press Enter.\r\n"); }
                    catch { }

                    _ = Task.Run(() => ClientReadLoopAsync(client, token));
                }
            }
            catch { }
        }

        private async Task ClientReadLoopAsync(Client client, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && client.IsConnected)
                {
                    string? line = await client.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null) break; // disconnected
                    var handler = CommandReceived;
                    if (handler != null)
                    {
                        try { await handler(line).ConfigureAwait(false); }
                        catch { }
                    }
                }
            }
            catch { }
            finally
            {
                try { RemoveClient(client); } catch { }
            }
        }

        private void RemoveClient(Client client)
        {
            lock (_gate) _clients.Remove(client);
            try { client.Dispose(); } catch { }
            SafeRaiseClientCountChanged();
        }

        private void SafeRaiseClientCountChanged()
        {
            try { ClientCountChanged?.Invoke(ClientCount); } catch { }
        }

        public void Dispose() => Stop();

        private sealed class Client : IDisposable
        {
            private readonly TcpClient _tcp;
            private readonly StreamReader _reader;
            private readonly StreamWriter _writer;
            private readonly Action<Client> _onError;

            public Client(TcpClient tcp, Action<Client> onError)
            {
                _tcp = tcp;
                _tcp.NoDelay = true;
                var stream = _tcp.GetStream();
                _reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                _writer = new StreamWriter(stream, new ASCIIEncoding()) { AutoFlush = true, NewLine = "\r\n" };
                _onError = onError;
            }

            public bool IsConnected => _tcp.Connected;

            public void WriteLine(string text)
            {
                try { _writer.WriteLine(text ?? string.Empty); }
                catch { _onError(this); throw; }
            }

            public async Task<string?> ReadLineAsync(CancellationToken token)
            {
                try { return await _reader.ReadLineAsync(token).ConfigureAwait(false); }
                catch { _onError(this); return null; }
            }

            public void Dispose()
            {
                try { _writer.Dispose(); } catch { }
                try { _reader.Dispose(); } catch { }
                try { _tcp.Close(); } catch { }
            }
        }
    }
}
