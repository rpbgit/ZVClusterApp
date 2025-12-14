using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    /// <summary>
    /// Icom CI-V driver. CI-V is a binary protocol: FE FE [to] [from] [cmd] [data...] FD.
    /// This driver keeps a high-level API and builds CI-V frames internally per model profile.
    /// </summary>
    public sealed class IcomCivDriver : SerialRadioDriverBase
    {
        // Manufacturer label
        public override string Manufacturer => "Icom";

        // CI-V address varies per model (default commonly 0x94 for IC-7300)
        public byte IcomAddress { get; set; } = 0x94;

        // Selected model ID (affects CI-V profile)
        private string _modelId = "IC-7300";
        public override string ModelId
        {
            get => _modelId;
            set
            {
                var newId = string.IsNullOrWhiteSpace(value) ? _modelId : value.Trim();
                if (!string.Equals(_modelId, newId, StringComparison.OrdinalIgnoreCase))
                {
                    _modelId = newId;
                    SelectProfile(_modelId);
                    Disconnect();
                    Debug.WriteLine($"[CAT:Icom] Model set: {_modelId}, CI-V=0x{IcomAddress:X2}");
                }
            }
        }

        // Current CI-V profile
        private CivProfile _profile = CivProfile.Default7300();

        private void SelectProfile(string id)
        {
            var map = new Dictionary<string, Func<CivProfile>>(StringComparer.OrdinalIgnoreCase)
            {
                ["IC-7300"] = CivProfile.Default7300,
                ["IC-705"]  = CivProfile.Default705,
                ["IC-7610"] = CivProfile.Default7610
            };
            _profile = map.TryGetValue(id, out var factory) ? factory() : CivProfile.Default7300();

            // Optional: adjust default CI-V address heuristically
            IcomAddress = id.ToUpperInvariant() switch
            {
                "IC-705" => 0xA4,
                "IC-7610" => 0x98,
                _ => IcomAddress
            };
        }

        /// <summary>
        /// High-level set frequency/mode. Builds CI-V frames via profile and writes as binary.
        /// </summary>
        public override bool SetFrequencyAndMode(int frequencyHz, string? mode)
        {
            if (!Enabled) { Debug.WriteLine("[CAT] Icom: disabled"); return false; }
            try
            {
                var freqCmd = _profile.BuildSetFrequency(IcomAddress, Math.Max(0, frequencyHz));
                WriteBinary(freqCmd);

                if (!string.IsNullOrWhiteSpace(mode))
                {
                    var modeCmd = _profile.BuildSetMode(IcomAddress, (mode ?? string.Empty).Trim().ToUpperInvariant());
                    WriteBinary(modeCmd);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT:Icom] SetFrequencyAndMode failed: {ex.Message}");
                return false;
            }
        }

        // ----- Profile types -----
        private static class Civ
        {
            public const byte Pc = 0xE0; // PC address in CI-V
            public static byte[] Header(byte to) => new[] { (byte)0xFE, (byte)0xFE, to, Pc };
            public static byte[] Trailer() => new[] { (byte)0xFD };
        }

        private sealed class CivProfile
        {
            public Func<byte, int, byte[]> BuildSetFrequency { get; init; } = (addr, hz) => Array.Empty<byte>();
            public Func<byte, string, byte[]> BuildSetMode { get; init; } = (addr, mode) => Array.Empty<byte>();

            private static byte[] HzToBcd5(int hz)
            {
                // Convert Hz to 10 ASCII digits, pack into 5 BCD bytes LSB-first per CI-V conventions.
                var s = Math.Max(0, hz).ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
                var bytes = new byte[5];
                for (int i = 0; i < 5; i++)
                {
                    int idx = s.Length - 2 * (i + 1);
                    int tens = idx >= 0 ? s[idx] - '0' : 0;
                    int ones = idx + 1 >= 0 ? s[idx + 1] - '0' : 0;
                    bytes[i] = (byte)((tens << 4) | (ones & 0x0F));
                }
                return bytes;
            }

            public static CivProfile Default7300() => new CivProfile
            {
                BuildSetFrequency = (addr, hz) =>
                {
                    var header = Civ.Header(addr);
                    var payload = HzToBcd5(hz);
                    var frame = new byte[header.Length + 1 + payload.Length + 1];
                    int i = 0; Array.Copy(header, 0, frame, i, header.Length); i += header.Length;
                    frame[i++] = 0x05; // Set freq
                    Array.Copy(payload, 0, frame, i, payload.Length); i += payload.Length;
                    Array.Copy(Civ.Trailer(), 0, frame, i, 1);
                    return frame;
                },
                BuildSetMode = (addr, mode) =>
                {
                    byte code = mode switch
                    {
                        "LSB" => (byte)0x00,
                        "USB" => (byte)0x01,
                        "AM"  => (byte)0x02,
                        "CW"  => (byte)0x03,
                        "FM"  => (byte)0x05,
                        "DATA" or "DAT" => (byte)0x07,
                        _ => (byte)0x01
                    };
                    var header = Civ.Header(addr);
                    var frame = new byte[header.Length + 3 + 1];
                    int i = 0; Array.Copy(header, 0, frame, i, header.Length); i += header.Length;
                    frame[i++] = 0x06; // Set mode
                    frame[i++] = code; // Mode code
                    // zv too many zero's frame[i++] = 0x00; // Filter placeholder
                    Array.Copy(Civ.Trailer(), 0, frame, i, 1);
                    return frame;
                }
            };

            public static CivProfile Default705() => Default7300();
            public static CivProfile Default7610() => Default7300();
        }
    }
}
