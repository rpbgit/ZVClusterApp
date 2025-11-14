using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Reflection; // for version info
using System.Text.RegularExpressions; // for version normalization

namespace ZVClusterApp.WinForms
{
    internal static class Program
    {
        // Central timestamp format (UTC)
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// Trace listener that prefixes all writes with a UTC timestamp using TimestampFormat.
        /// </summary>
        private sealed class TimestampTraceListener : TextWriterTraceListener
        {
            public TimestampTraceListener(TextWriter writer, string name) : base(writer, name) { }
            private static string Stamp() => DateTime.UtcNow.ToString(TimestampFormat);
            public override void Write(string? message) => base.Write($"{Stamp()} {message}");
            public override void WriteLine(string? message) => base.WriteLine($"{Stamp()} {message}");
        }

        /// <summary>
        /// Version string resolution identical to About dialog logic; strips +metadata and trailing paren groups.
        /// </summary>
        private static string GetAppVersion() 
        {
            static string NormalizeVer(string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return string.Empty;
                var s = v.Trim();
                var plus = s.IndexOf('+');
                if (plus >= 0) s = s[..plus];
                s = Regex.Replace(s, @"\s*\(.*\)$", string.Empty);
                return s;
            }
            try
            {
                var entry = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var infoVerRaw = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                string? productVerRaw = null, fileVerRaw = null;
                try
                {
                    var exePath = Environment.ProcessPath; if (string.IsNullOrEmpty(exePath)) exePath = Application.ExecutablePath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(exePath);
                        productVerRaw = fvi.ProductVersion; fileVerRaw = fvi.FileVersion;
                    }
                }
                catch { }
                var asmVerRaw = entry.GetName().Version?.ToString();
                var appProductVerRaw = Application.ProductVersion;
                var infoVer = NormalizeVer(infoVerRaw);
                if (string.IsNullOrEmpty(infoVer)) infoVer = NormalizeVer(appProductVerRaw);
                if (string.IsNullOrEmpty(infoVer)) infoVer = NormalizeVer(productVerRaw);
                if (string.IsNullOrEmpty(infoVer)) infoVer = NormalizeVer(fileVerRaw);
                if (string.IsNullOrEmpty(infoVer)) infoVer = NormalizeVer(asmVerRaw);
                return string.IsNullOrEmpty(infoVer) ? "(unknown)" : infoVer;
            }
            catch { return "(unknown)"; }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var settings = AppSettings.Load(); 

            TextWriterTraceListener? listener = null; // timestamp listener
            StreamWriter? logWriter = null;
            try
            {
                if (settings.DebugLogEnabled || Debugger.IsAttached)
                {
                    var logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    logWriter = new StreamWriter(logPath, append: true, Encoding.UTF8) { AutoFlush = true };
                    listener = new TimestampTraceListener(logWriter, "DebugLog");
                    Trace.Listeners.Add(listener);
                    Trace.WriteLine("==== Application start ====");
                    Trace.WriteLine($"Version: {GetAppVersion()}");
                }

                using var radio = new RadioController(settings.CatPort, settings.CatBaud, settings.CatEnabled)
                {
                    Rig = settings.Rig,
                    IcomAddress = settings.IcomAddress
                };
                var clusterManager = new ClusterManager(settings);
                Application.Run(new MainForm(clusterManager, radio, settings));
            }
            finally
            {
                try { Trace.WriteLine("==== Application exit ===="); } catch { }
                try { Trace.Flush(); } catch { }
                if (listener != null)
                {
                    try { Trace.Listeners.Remove(listener); } catch { }
                    try { listener.Flush(); listener.Close(); } catch { }
                }
                try { logWriter?.Dispose(); } catch { }
            }
        }
    }
}
