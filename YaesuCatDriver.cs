using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    public sealed class YaesuCatDriver : SerialRadioDriverBase
    {
        public override bool SetFrequencyAndMode(int frequencyHz, string? mode)
        {
            if (!Enabled) { Debug.WriteLine("[CAT] Yaesu: disabled"); return false; }
            try
            {
                EnsureOpen();
                var freq = Math.Abs(frequencyHz).ToString("D11", CultureInfo.InvariantCulture);
                WriteAscii($"FA{freq};");
                Debug.WriteLine($"[CAT] Yaesu FREQ: FA{freq};");

                var md = MapYaesuMode(mode);
                if (md != null)
                {
                    Thread.Sleep(100); // small inter-command delay
                    WriteAscii($"MD{md};");
                    Debug.WriteLine($"[CAT] Yaesu MODE: MD{md};");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT] Yaesu send failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void WriteAscii(string cmd)
        {
            _serial!.Write(cmd);
        }

        private static string? MapYaesuMode(string? mode)
        {
            switch ((mode ?? string.Empty).ToUpperInvariant())
            {
                case "LSB": return "01";
                case "USB": return "02";
                case "CW":  return "03";
                case "FM":  return "04";
                case "AM":  return "05";
                case "RTTY":return "06";
                case "DAT": return "12"; // USB-D fallback
                default: return null;
            }
        }
    }
}
