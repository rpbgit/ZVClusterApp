using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    public abstract class SerialRadioDriverBase : IRadioDriver
    {
        protected SerialPort? _serial;

        public bool Enabled { get; set; }
        public string Port { get; set; } = "COM1";
        public int Baud { get; set; } = 19200;

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
                    Debug.WriteLine($"[CAT] Disconnect: Port={_serial.PortName}, IsOpen={_serial.IsOpen}");
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
    }
}
