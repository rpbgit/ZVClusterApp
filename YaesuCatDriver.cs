using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    /// <summary>
    /// Yaesu CAT driver: high-level operations only. Internally selects model-specific
    /// CAT formatting (ASCII or legacy binary frames) and dispatches through base helpers.
    /// </summary>
    public sealed class YaesuCatDriver : SerialRadioDriverBase
    {
        // Manufacturer label used by UI/diagnostics
        public override string Manufacturer => "Yaesu";

        // Selected model ID (affects command format/profile)
        private string _modelId = "FT-991A";
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
                    // Apply-on-next-use
                    Disconnect();
                    Debug.WriteLine($"[CAT:Yaesu] Model set: {_modelId}");
                }
            }
        }

        // Current CAT profile for the selected model
        private CatProfile _profile = CatProfile.Default991A();

        // Map model IDs to shared profiles (many models share CAT)
        private void SelectProfile(string id)
        {
            var map = new Dictionary<string, Func<CatProfile>>(StringComparer.OrdinalIgnoreCase)
            {
                ["FT-991"]  = CatProfile.Default991A,
                ["FT-991A"] = CatProfile.Default991A,
                ["FT-891"]  = CatProfile.Default891,
                ["FT-857"]  = CatProfile.Legacy857_897,
                ["FT-897"]  = CatProfile.Legacy857_897,
            };
            // if i cant find a match, default to 991A style "FA" commands, most modern yaesu's use this 
            _profile = map.TryGetValue(id, out var factory) ? factory() : CatProfile.Default991A();
        }

        /// <summary>
        /// High-level set frequency/mode. Formatting (ASCII/Binary) is decided by the profile.
        /// </summary>
        public override bool SetFrequencyAndMode(int frequencyHz, string? mode)
        {
            if (!Enabled) { Debug.WriteLine("[CAT] Yaesu: disabled"); return false; }
            try
            {
                var freqCmd = _profile.BuildSetFrequency(Math.Max(0, frequencyHz));
                Dispatch(freqCmd);

                if (!string.IsNullOrWhiteSpace(mode))
                {
                    PaceBetweenCommands();
                    var modeCmd = _profile.BuildSetMode((mode ?? string.Empty).Trim().ToUpperInvariant());
                    Dispatch(modeCmd);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT:Yaesu] SetFrequencyAndMode failed: {ex.Message}");
                return false;               
            }
        }

        // Dispatch a profile command via ASCII or Binary write helpers
        private void Dispatch(ProfileCommand cmd)
        {
            if (cmd.Kind == CommandKind.Ascii)
                WriteAscii(cmd.AsciiText ?? string.Empty, cmd.AsciiTerminator ?? string.Empty);
            else
                WriteBinary(cmd.BinaryPayload ?? Array.Empty<byte>());
        }

        // ----- Profile types -----
        private enum CommandKind { Ascii, Binary }

        private sealed class ProfileCommand
        {
            public CommandKind Kind { get; init; }
            public string? AsciiText { get; init; }
            public string? AsciiTerminator { get; init; }
            public byte[]? BinaryPayload { get; init; }
        }

        private sealed class CatProfile
        {
            public Func<int, ProfileCommand> BuildSetFrequency { get; init; } = _ => new ProfileCommand { Kind = CommandKind.Ascii, AsciiText = string.Empty, AsciiTerminator = ";" };
            public Func<string, ProfileCommand> BuildSetMode { get; init; } = _ => new ProfileCommand { Kind = CommandKind.Ascii, AsciiText = "MD0;", AsciiTerminator = string.Empty };

            // FT-991/991A/891 ASCII CAT with ';'
            public static CatProfile Default991A() => new CatProfile
            {
                BuildSetFrequency = hz => new ProfileCommand { Kind = CommandKind.Ascii, AsciiText = $"FA{hz:00000000000};" },
                BuildSetMode = mode => new ProfileCommand
                {
                    Kind = CommandKind.Ascii,
                    AsciiText = mode switch
                    {
                        "LSB" => "MD1;",
                        "USB" => "MD2;",
                        "CW"  => "MD3;",
                        "FM"  => "MD4;",
                        "AM"  => "MD5;",
                        "RTTY"=> "MD6;",
                        "DAT" or "DATA" => "MD6;",
                        _ => "MD2;"
                    }
                }
            };

            public static CatProfile Default891() => Default991A();

            // Legacy 857/897 binary frequency command:
            // - Frequency is encoded in 10 Hz units as 8 decimal digits (zero-padded).
            // - Packed into 4 BCD bytes (each byte holds two digits).
            // - Command ID 0x01 appended at the end.
            // Example: 439.70 MHz (439,700,000 Hz) -> 43 97 00 00 + 01
            // Example: 14.23456 Mhz -> 01 42 34 56 + 01 (opcode)
            public static CatProfile Legacy857_897() => new CatProfile {
                BuildSetFrequency = hz => {
                    // Convert Hz to 10 Hz units and format to 8 digits
                    var units10Hz = Math.Max(0, hz / 10);
                    var s = units10Hz.ToString("D8", CultureInfo.InvariantCulture); // e.g., "43970000"

                    // Pack into 4 BCD bytes: "01" "42" "34" "56"
                    byte PackPair(int i) => (byte)(((s[i] - '0') << 4) | (s[i + 1] - '0'));
                    var b0 = PackPair(0);
                    var b1 = PackPair(2);
                    var b2 = PackPair(4);
                    var b3 = PackPair(6);

                    // Append command ID 0x01
                    var frame = new byte[] { b0, b1, b2, b3, 0x01 };

                    return new ProfileCommand { Kind = CommandKind.Binary, BinaryPayload = frame };
                },
                BuildSetMode = mode => {
                    // Minimal mapping; verify codes against FT-857/897 CAT manual
                    byte code = mode switch
                    {
                        "LSB" => 0x00,
                        "USB" => 0x01,
                        "CW"  => 0x02,
                        "FM"  => 0x08,
                        "AM"  => 0x04,
                        "DATA" or "DAT" => 0x0A, // placeholder; confirm
                        _ => 0x01
                    };
                    // Legacy CAT mode command frame: [mode] + [00 00 00] + [07] (mode opcode)
                    var frame = new byte[] { code, 0x00, 0x00, 0x00, 0x07 };
                    return new ProfileCommand { Kind = CommandKind.Binary, BinaryPayload = frame };
                }
            };
        }
    }
}
