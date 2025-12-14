using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    public abstract class SerialRadioDriverBase : IRadioDriver
    {
        protected SerialPort? _serial;

        // Backing fields with sensible defaults
        private bool _enabled;
        private string _port = "COM1";
        private int _baud = 19200;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                // Any toggle of Enabled forces a disconnect; we keep lazy connect semantics.
                Disconnect();
            }
        }

        public string Port
        {
            get => _port;
            set
            {
                var newPort = string.IsNullOrWhiteSpace(value) ? "COM1" : value.Trim();
                if (string.Equals(_port, newPort, StringComparison.OrdinalIgnoreCase)) return;
                _port = newPort;
                // Changing port while open requires a reconnect with new settings.
                Disconnect();
            }
        }

        public int Baud
        {
            get => _baud;
            set
            {
                var newBaud = value <= 0 ? 19200 : value;
                if (_baud == newBaud) return;
                _baud = newBaud;
                // Changing baud while open requires a reconnect with new settings.
                Disconnect();
            }
        }

        // New: identity must be provided by concrete drivers so they can
        // choose the correct CAT/CIV profile and formatting internally.
        public abstract string Manufacturer { get; }
        public abstract string ModelId { get; set; }

        public virtual bool Connect()
        {
            Debug.WriteLine($"[CAT] Connect: Enabled={Enabled}, Port={Port}, Baud={Baud}");
            if (!Enabled) { Debug.WriteLine("[CAT] Connect aborted: disabled"); return false; }
            try
            {
                _serial = new SerialPort(Port, Baud)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None
                };
                _serial.Open();
                Debug.WriteLine("[CAT] Port opened");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT] Connect failed: {ex.GetType().Name}: {ex.Message}");
                _serial = null; return false;
            }
        }

        public virtual void Disconnect()
        {
            try
            {
                if (_serial != null)
                {
                    Debug.WriteLine($"[CAT] Disconnect: Port={_serial.PortName}, IsOpen={_serial.IsOpen}, Baud={Baud}");
                    try { _serial.Close(); } catch (Exception ex) { Debug.WriteLine($"[CAT] Close error: {ex.Message}"); }
                }
            }
            catch { }
            finally { _serial = null; }
        }

        public abstract bool SetFrequencyAndMode(int frequencyHz, string? mode);

        public void Dispose() => Disconnect();

        protected void EnsureOpen()
        {
            if (_serial == null || !_serial.IsOpen)
            {
                if (!Connect()) throw new InvalidOperationException("Port not open");
            }
        }

        // Helper: write ASCII CAT, optionally appending a terminator (e.g., ';' or CRLF).
        protected void WriteAscii(string command, string terminator = "")
        {
            EnsureOpen();
            if (_serial == null) throw new InvalidOperationException("Port not open");
            var payload = command ?? string.Empty;
            if (!string.IsNullOrEmpty(terminator) && !payload.EndsWith(terminator, StringComparison.Ordinal))
                payload += terminator;
            // Log exactly what will be sent (ASCII)
            try
            {
                Debug.WriteLine($"[CAT:WRITE ASCII] Port={_serial.PortName} Baud={_serial.BaudRate} Data='{payload.Replace("\r", "\\r").Replace("\n", "\\n")}'");
            }
            catch { }
            _serial.Write(payload);
        }

        // Helper: write binary CAT/CIV payload (raw bytes).
        protected void WriteBinary(ReadOnlySpan<byte> payload)
        {
            EnsureOpen();
            if (_serial == null) throw new InvalidOperationException("Port not open");
            // Log exactly what will be sent (HEX bytes)
            try
            {
                var hex = string.Join(" ", payload.ToArray().Select(b => b.ToString("X2")));
                Debug.WriteLine($"[CAT:WRITE BIN] Port={_serial.PortName} Baud={_serial.BaudRate} Bytes=[{hex}]");
            }
            catch { }
            _serial.BaseStream.Write(payload);
        }
    }
}
