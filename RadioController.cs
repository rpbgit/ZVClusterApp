using System;
using System.Diagnostics;
using System.IO.Ports;

namespace ZVClusterApp.WinForms
{
    public enum RigType { Unknown, Icom, Yaesu, Kenwood }

    public class RadioController : IDisposable
    {
        private IRadioDriver _driver;
        private RigType _rig;
        private string _modelId = "IC-7300"; // default model selection

        public RadioController(string port, int baud, bool enabled)
        {
            _rig = RigType.Icom; // default
            _driver = CreateDriver(_rig, port, baud, enabled);
            Debug.WriteLine($"[Radio] Controller created. Rig={_rig}, Enabled={enabled}, Port={port}, Baud={baud}");
        }

        // Rig selection
        public RigType Rig
        {
            get => _rig;
            set
            {
                if (_rig == value) return;
                Debug.WriteLine($"[Radio] Rig changing: {_rig} -> {value}");
                var old = _driver;
                _driver = CreateDriver(value, old.Port, old.Baud, old.Enabled);
                // Preserve model selection across driver switches
                try { _driver.ModelId = _modelId; } catch { }
                try { old.Dispose(); } catch { }
                _rig = value;
            }
        }

        // Icom-only property passthrough
        public byte IcomAddress
        {
            get => (_driver as IcomCivDriver)?.IcomAddress ?? 0x94;
            set { if (_driver is IcomCivDriver ic) ic.IcomAddress = value; }
        }

        // Dynamic properties
        public bool Enabled { get => _driver.Enabled; set => _driver.Enabled = value; }
        public string Port { get => _driver.Port; set => _driver.Port = value; }
        public int Baud { get => _driver.Baud; set => _driver.Baud = value; }

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
                if (_driver is SerialRadioDriverBase srb) srb.StopBits = (value == 2) ? 2 : 1;
            }
        }

        // Selected model ID facade for settings dialog
        public string ModelId
        {
            get => _modelId;
            set
            {
                _modelId = value ?? _modelId;
                try { _driver.ModelId = _modelId; } catch { }
            }
        }

        public bool Connect() => _driver.Connect();
        public void Disconnect() => _driver.Disconnect();

        public bool SendFrequency(int frequencyHz, string? mode = null)
        {
            Debug.WriteLine($"[Radio] SendFrequency facade: {frequencyHz} Hz, mode='{mode}' via {_rig}");
            return _driver.SetFrequencyAndMode(frequencyHz, mode);
        }

        // Helper to apply settings after the Settings dialog saves
        public void ApplySettings(AppSettings s)
        {
            // Apply CAT/serial settings to the driver (includes pacing delay)
            if (_driver is SerialRadioDriverBase serial)
                serial.ApplySettings(s);
            else
            {
                // Fallback (shouldn't happen with current drivers)
                Enabled = s.CatEnabled;
                Port = s.CatPort;
                Baud = s.CatBaud;
                StopBits = (s.CatStopBits == System.IO.Ports.StopBits.Two) ? 2 : 1;
            }

            Rig = s.Rig;
            ModelId = s.CatModelId;

            // Icom CI-V address (Icom only)
            IcomAddress = s.IcomAddress;
        }

        private static IRadioDriver CreateDriver(RigType rig, string port, int baud, bool enabled)
        {
            SerialRadioDriverBase drv = rig switch
            {
                RigType.Icom => new IcomCivDriver(),
                RigType.Kenwood => new KenwoodCatDriver(),
                RigType.Yaesu => new YaesuCatDriver(),
                _ => new IcomCivDriver(),
            };
            drv.Port = port;
            drv.Baud = baud;
            drv.Enabled = enabled;
            // Apply persisted StopBits to the driver
            //try { drv.StopBits = AppSettings.Load().CatStopBits == System.IO.Ports.StopBits.Two ? 2 : 1; } catch { drv.StopBits = 1; }
            return drv;
        }

        public void Dispose()
        {
            try { _driver.Dispose(); } catch { }
        }
    }
}
