using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    /// <summary>
    /// Kenwood CAT driver. Kenwood rigs typically use ASCII CAT with ';' or CRLF.
    /// This driver keeps a high-level API and selects per-model ASCII formatting via profiles.
    /// </summary>
    public sealed class KenwoodCatDriver : SerialRadioDriverBase
    {
        public override string Manufacturer => "Kenwood";

        private string _modelId = "TS-590SG";
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
                    Debug.WriteLine($"[CAT:Kenwood] Model set: {_modelId}");
                }
            }
        }

        private KenProfile _profile = KenProfile.DefaultTS590();

        private void SelectProfile(string id)
        {
            var map = new Dictionary<string, Func<KenProfile>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TS-590"]   = KenProfile.DefaultTS590,
                ["TS-590S"]  = KenProfile.DefaultTS590,
                ["TS-590SG"] = KenProfile.DefaultTS590,
                ["TS-2000"]  = KenProfile.DefaultTS2000,
                ["TS-480"]   = KenProfile.DefaultTS480,
                ["TS-890"]   = KenProfile.DefaultTS590, // shares FA/MD ASCII CAT
                ["TS-990"]   = KenProfile.DefaultTS590  // shares FA/MD ASCII CAT
            };
            _profile = map.TryGetValue(id, out var factory) ? factory() : KenProfile.DefaultTS590();
        }

        public override bool SetFrequencyAndMode(int frequencyHz, string? mode)
        {
            if (!Enabled) { Debug.WriteLine("[CAT] Kenwood: disabled"); return false; }
            try
            {
                var freqCmd = _profile.BuildSetFrequency(Math.Max(0, frequencyHz));
                WriteAscii(freqCmd, _profile.Terminator);

                if (!string.IsNullOrWhiteSpace(mode))
                {
                    var modeCmd = _profile.BuildSetMode((mode ?? string.Empty).Trim().ToUpperInvariant());
                    WriteAscii(modeCmd, _profile.Terminator);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT:Kenwood] SetFrequencyAndMode failed: {ex.Message}");
                return false;
            }
        }

        private sealed class KenProfile
        {
            // Kenwood ASCII terminator preference (some use CRLF, here we append ';' and allow CRLF via SerialPort.NewLine)
            public string Terminator { get; init; } = string.Empty;
            public Func<int, string> BuildSetFrequency { get; init; } = hz => string.Empty;
            public Func<string, string> BuildSetMode { get; init; } = mode => string.Empty;

            public static KenProfile DefaultTS590() => new KenProfile
            {
                Terminator = string.Empty, // commands already include ';'
                BuildSetFrequency = hz => $"FA{hz:00000000000};",
                BuildSetMode = mode => mode switch
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
            };

            public static KenProfile DefaultTS2000() => DefaultTS590();
            public static KenProfile DefaultTS480() => DefaultTS590();
        }
    }
}
