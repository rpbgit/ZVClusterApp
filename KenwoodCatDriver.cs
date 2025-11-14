using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    public sealed class KenwoodCatDriver : SerialRadioDriverBase
    {
        public override bool SetFrequencyAndMode(int frequencyHz, string? mode)
        {
            if (!Enabled) { Debug.WriteLine("[CAT] Kenwood: disabled"); return false; }
            try
            {
                EnsureOpen();
                var freq = Math.Abs(frequencyHz).ToString("D11", CultureInfo.InvariantCulture);
                WriteAscii($"FA{freq};");
                Debug.WriteLine($"[CAT] Kenwood FREQ: FA{freq};");

                var md = MapKenwoodMode(mode);
                if (md != null)
                {
                    Thread.Sleep(100); // small inter-command delay
                    WriteAscii($"MD{md};");
                    Debug.WriteLine($"[CAT] Kenwood MODE: MD{md};");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT] Kenwood send failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void WriteAscii(string cmd)
        {
            _serial!.Write(cmd);
        }

        private static string? MapKenwoodMode(string? mode)
        {
            switch ((mode ?? string.Empty).ToUpperInvariant())
            {
                case "LSB": return "1";
                case "USB": return "2";
                case "CW": return "3";
                case "FM": return "4";
                case "AM": return "5";
                case "RTTY": return "6";
                case "DAT": return "2"; // USB as data fallback
                default: return null;
            }
        }
    }
}
