using System;
using System.Diagnostics;

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
        // Inhibit mode : pass null instead of mode based on a setting/flag
        //var effectiveMode = null; // AppSettings.Current?.InhibitModeSend == true ? null : mode;
        //return _driver.SetFrequencyAndMode(frequencyHz, effectiveMode);
            return _driver.SetFrequencyAndMode(frequencyHz, mode);
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
            drv.Port = port; drv.Baud = baud; drv.Enabled = enabled;
            return drv;
        }

        public void Dispose()
        {
            try { _driver.Dispose(); } catch { }
        }
    }
}
