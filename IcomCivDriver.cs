using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace ZVClusterApp.WinForms
{
    public sealed class IcomCivDriver : SerialRadioDriverBase
    {
        public byte IcomAddress { get; set; } = 0x94;
        private const byte CivPcAddress = 0xE0;

        public override bool SetFrequencyAndMode(int frequencyHz, string? mode)
        {
            if (!Enabled) { Debug.WriteLine("[CAT] Icom: disabled"); return false; }
            try
            {
                EnsureOpen();
                // Frequency frame (0x05)
                var freqPayload = BuildIcomFrequencyPayload(frequencyHz);
                var freqFrame = BuildIcomFrame(IcomAddress, 0x05, freqPayload);
                _serial!.Write(freqFrame, 0, freqFrame.Length);
                Debug.WriteLine($"[CAT] Icom FREQ: {Hex(freqFrame)}");

                if (!string.IsNullOrWhiteSpace(mode))
                {
                    // Small inter-command delay
                    Thread.Sleep(100);

                    var (modeCode, filter) = MapIcomMode(mode!);
                    var modeFrame = BuildIcomFrame(IcomAddress, 0x06, new byte[] { modeCode, filter });
                    _serial.Write(modeFrame, 0, modeFrame.Length);
                    Debug.WriteLine($"[CAT] Icom MODE: {Hex(modeFrame)}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CAT] Icom send failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static byte[] BuildIcomFrame(byte toAddress, byte cmd, ReadOnlySpan<byte> payload)
        {
            var buf = new byte[5 + payload.Length + 1];
            int i = 0;
            buf[i++] = 0xFE; buf[i++] = 0xFE;
            buf[i++] = toAddress;
            buf[i++] = CivPcAddress;
            buf[i++] = cmd;
            for (int p = 0; p < payload.Length; p++) buf[i++] = payload[p];
            buf[i++] = 0xFD;
            return buf;
        }

        private static byte[] BuildIcomFrequencyPayload(int frequencyHz)
        {
            var s = Math.Abs(frequencyHz).ToString("D10", CultureInfo.InvariantCulture); // 10 digits
            var bytes = new byte[5];
            for (int i = 0; i < 5; i++)
            {
                int start = s.Length - 2 * (i + 1);
                int tens = start >= 0 ? s[start] - '0' : 0;
                int ones = start + 1 >= 0 ? s[start + 1] - '0' : 0;
                bytes[i] = (byte)((tens << 4) | (ones & 0x0F));
            }
            return bytes;
        }

        private static (byte mode, byte filter) MapIcomMode(string mode)
        {
            switch ((mode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "LSB": return (0x00, 0x01);
                case "USB": return (0x01, 0x01);
                case "AM": return (0x02, 0x01);
                case "CW": return (0x03, 0x01);
                case "RTTY": return (0x04, 0x01);
                case "FM": return (0x05, 0x01);
                case "DATA": return (0x01, 0x01); // USB fallback
                default: return (0x01, 0x01);
            }
        }

        private static string Hex(byte[] data)
        {
            return BitConverter.ToString(data).Replace('-', ' ');
        }
    }
}
