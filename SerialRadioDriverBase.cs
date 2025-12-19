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
        private global::System.IO.Ports.StopBits _stopBits = global::System.IO.Ports.StopBits.One;

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
                Debug.WriteLine($"[CAT] Port changing: {_port} -> {newPort}");
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
                Debug.WriteLine($"[CAT] Baud changing: {_baud} -> {newBaud}");
                _baud = newBaud;
                // Changing baud while open requires a reconnect with new settings.
                Disconnect();
            }
        }

        // Configurable StopBits as integer (1 or 2). Changing while open triggers reconnect.
        public int StopBits
        {
            get => _stopBits == global::System.IO.Ports.StopBits.Two ? 2 : 1;
            set
            {
                var newEnum = value == 2 ? global::System.IO.Ports.StopBits.Two : global::System.IO.Ports.StopBits.One;
                if (_stopBits == newEnum) return;
                Debug.WriteLine($"[CAT] StopBits changing: {(StopBits == 2 ? 2 : 1)} -> {(value == 2 ? 2 : 1)}");
                _stopBits = newEnum;
                Disconnect();
            }
        }

        // New: identity must be provided by concrete drivers so they can
        // choose the correct CAT/CIV profile and formatting internally.
        public abstract string Manufacturer { get; }
        public abstract string ModelId { get; set; }

        public virtual bool Connect()
        {
            // Resolve StopBits once for both logging and opening
            var stopBitsInt = StopBits;
            var stopBitsEnum = _stopBits;
            Debug.WriteLine($"[CAT] Connect: Enabled={Enabled}, Port={Port}, Baud={Baud}, Parity={Parity.None}, DataBits={8}, StopBits={stopBitsInt}, Handshake={Handshake.None}, ReadTimeout={500}, WriteTimeout={500}");
            if (!Enabled) { Debug.WriteLine("[CAT] Connect aborted: disabled"); return false; }
            try
            {
                _serial = new SerialPort(Port, Baud)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = stopBitsEnum,
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
                var sp = _serial; // capture reference
                if (sp != null)
                {
                    Debug.WriteLine($"[CAT] Disconnect: Port={sp.PortName}, IsOpen={sp.IsOpen}, Baud={Baud}");
                    // Ensure subsequent calls see null immediately
                    _serial = null;
                    Debug.WriteLine("[CAT] Disconnect: _serial set to null");
                    try
                    {
                        sp.Close();
                        Debug.WriteLine("[CAT] Port closed");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[CAT] Close error: {ex.Message}");
                    }
                } else {
                    Debug.WriteLine("[CAT] Disconnect: _serial was already null");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT] Disconnect unexpected error: {ex.Message}");
            }
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

        // New: ApplySettings method to update properties from AppSettings.
        public void ApplySettings(AppSettings s)
        {
            // Map AppSettings CAT section to driver properties
            Enabled = s.CatEnabled;
            Port = s.CatPort;
            Baud = s.CatBaud;
            // Driver StopBits expects 1 or 2; settings store enum
            StopBits = s.CatStopBits == global::System.IO.Ports.StopBits.Two ? 2 : 1;
        }
    }
}
