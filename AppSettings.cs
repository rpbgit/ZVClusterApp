using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZVClusterApp.WinForms
{
    public enum ClusterFormat
    {
        Auto = 0,
        DXSpider = 1,
        ARCluster = 2,
        CCCluster = 3
    }

    public record ClusterDefinition
    {
        public string Name { get; init; } = "Unnamed";
        public string Host { get; init; } = "";
        public int Port { get; init; } = 7000;
        public bool AutoLogin { get; init; } = false;
        public string[] DefaultCommands { get; init; } = Array.Empty<string>();
        public ClusterFormat Format { get; set; } = ClusterFormat.Auto;
    }

    public class UiSettings
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int WindowState { get; set; } // cast of FormWindowState
        public int SplitterDistance { get; set; }
        public int[] ColumnWidths { get; set; } = Array.Empty<int>();
        public string[] EnabledBands { get; set; } = Array.Empty<string>();
        public string[] EnabledModes { get; set; } = new[] { "CW", "SSB", "DAT" };

        // Console appearance
        public string ConsoleFontFamily { get; set; } = "Consolas";
        public float ConsoleFontSize { get; set; } = 9.0f;
        public string ConsoleForeColor { get; set; } = "#00FF00"; // Lime
        public string ConsoleBackColor { get; set; } = "#000000"; // Black

        // DX list appearance (font)
        public string? DxListFontFamily { get; set; }
        public float? DxListFontSize { get; set; }
        public FontStyle DxListFontStyle { get; set; } = FontStyle.Regular;
    }

    public class AppSettings
    {
        public List<ClusterDefinition> Clusters { get; set; } = new();

        // Persist last successfully connected cluster name
        public string? LastConnectedCluster { get; set; } = null;

        // Local server
        public int LocalServerPort { get; set; } = 7373;
        public bool LocalServerEnabled { get; set; } = true;

        // Debug logging toggle (opt-in)
        public bool DebugLogEnabled { get; set; } = false;

        // User profile
        public string MyCall { get; set; } = "MYCALL";
        public string Name { get; set; } = string.Empty;
        public int GmtOffsetHours { get; set; } = 0; // +/- hours
        public string QTH { get; set; } = string.Empty;
        public string GridSquare { get; set; } = string.Empty;

        // Spot sources (max 4)
        public string[] MyClusters { get; set; } = new[] { "MYCALL", "", "", "" };

        // Lowest frequency first
        public string[] TrackBands { get; set; } = new[] { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m" };
        public bool TrackFromUs { get; set; } = true;
        public bool TrackFromDx { get; set; } = true;

        public bool CatEnabled { get; set; } = false;
        public string CatPort { get; set; } = "COM1";
        public int CatBaud { get; set; } = 19200;
        public RigType Rig { get; set; } = RigType.Icom;

        public byte IcomAddress { get; set; } = 0x94;

        // Distance unit preference: false = statute miles (default), true = kilometers
        public bool UseKilometers { get; set; } = false;

        public Dictionary<string, string> BandColors { get; set; } = new()
        {
            ["160m"] = "#4E342E", // brown
            ["80m"]  = "#9C27B0", // purple
            ["60m"]  = "#009688", // teal
            ["40m"]  = "#2196F3", // blue
            ["30m"]  = "#00BCD4", // cyan
            ["20m"]  = "#4CAF50", // green
            ["17m"]  = "#8BC34A", // light green
            ["15m"]  = "#FFAF80", // pale orange try FFAF80 from chooser
            ["12m"]  = "#FFEB3B", // bright yellow (distinct from 15m/10m)
            ["10m"]  = "#D32F2F", // crimson red (distinct from 12m/15m)
            ["6m"]   = "#3F51B5", // indigo
        };

        public UiSettings? Ui { get; set; } = new UiSettings { Width = 810, Height = 494, SplitterDistance = 200 };

        // Settings window location persistence (absolute)
        public int SettingsLeft { get; set; } = 0;
        public int SettingsTop { get; set; } = 0;

        // Settings window relative offset from Main form
        public int SettingsOffsetX { get; set; } = 0;
        public int SettingsOffsetY { get; set; } = 0;

        // Data updates
        public bool CheckCtyOnBoot { get; set; } = true;

        private const string FileName = "ZVClusterApp.settings.json";
        private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, FileName);

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        // Ensure MyCalls has max 4 slots
                        if (s.MyClusters == null || s.MyClusters.Length == 0)
                            s.MyClusters = new[] { s.MyCall, "", "", "" };
                        else if (s.MyClusters.Length > 4)
                            s.MyClusters = s.MyClusters.Take(4).ToArray();
                        else if (s.MyClusters.Length < 4)
                            s.MyClusters = s.MyClusters.Concat(Enumerable.Repeat("", 4 - s.MyClusters.Length)).ToArray();

                        // Default server port if uninitialized
                        if (s.LocalServerPort <= 0 || s.LocalServerPort > 65535)
                            s.LocalServerPort = 7373;

                        // Migration: bump older default 7300 to 7373
                        if (s.LocalServerPort == 7300)
                            s.LocalServerPort = 7373;

                        // Normalize TrackBands: ensure canonical ordering and include 60m when appropriate
                        var knownBands = new[] { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m" };
                        if (s.TrackBands == null || s.TrackBands.Length == 0)
                        {
                            s.TrackBands = knownBands;
                        }
                        else
                        {
                            string Canon(string tok)
                            {
                                if (string.IsNullOrWhiteSpace(tok)) return string.Empty;
                                var t = Regex.Replace(tok.Trim().ToLowerInvariant(), "[^a-z0-9]", string.Empty);
                                if (Regex.IsMatch(t, "^\\d+$")) t += "m"; // '60' -> '60m'
                                return t;
                            }

                            var orig = s.TrackBands.Select(t => Canon(t)).Where(t => !string.IsNullOrWhiteSpace(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            var final = new List<string>();
                            foreach (var b in knownBands)
                            {
                                var bn = Canon(b);
                                if (orig.Contains(bn)) final.Add(b);
                                else if (bn == "60m" && orig.Contains("80m") && orig.Contains("40m")) final.Add("60m");
                            }
                            if (final.Count == 0) final.AddRange(knownBands);
                            s.TrackBands = final.ToArray();
                        }

                        return s;
                    }
                }
            }
            catch { }

            var def = new AppSettings();
            // set sensible defaults for the main spot list columns (DX, Freq, Spotter, Time, Country, Dist, Bearing, Info)
            def.Ui = new UiSettings {
                Width = 810,
                Height = 494,
                SplitterDistance = 200,
                ColumnWidths = new[] { 100, 80, 100, 60, 160, 70, 120, 165 }
            };
            // Add two default clusters; default AutoLogin set to false for all
            def.Clusters.Add(new ClusterDefinition
            {
                Name = "dxcluster.org",
                Host = "dxcluster.org",
                Port = 7000,
                AutoLogin = false,
                DefaultCommands = Array.Empty<string>(),
                Format = ClusterFormat.DXSpider
            });

            def.Clusters.Add(new ClusterDefinition
            {
                Name = "AI9T",
                Host = "dxc.ai9t.com",
                Port = 7300,
                AutoLogin = false,
                // One line per command sent after connect (lines starting with # are optional; keep/remove as you prefer)
                DefaultCommands = new[]
                {
                    "# useful Cluster commands, associated with your login and persists",
                    "#SET/NOFT8\t# no FT8 spots",
                    "SET/FILTER DXCTY/REJECT K,VE\t#ignore spots for countries K,VE",
                    "#SET/FILTER DXCTY/OFF\t#let spots thru for all countries",
                    "# ",
                    "SET/FILTER DOC/PASS K,VE\t#only pass spots that originate from K,VE",
                    "#SET/NOFILTER\t#clears all filters, all spots pass thru",
                    "SH/FILTER\t# show current filter settings",
                    "SH/MYDX/30\t# show me the last 30 spots to seed the display"
                },
                Format = ClusterFormat.CCCluster
            });
            Save(def);
            return def;
        }

        public static void Save(AppSettings s)
        {
            try
            {
                // Trim MyCalls to 4 entries
                if (s.MyClusters == null) s.MyClusters = new[] { s.MyCall, "", "", "" };
                else if (s.MyClusters.Length > 4) s.MyClusters = s.MyClusters.Take(4).ToArray();
                else if (s.MyClusters.Length < 4) s.MyClusters = s.MyClusters.Concat(Enumerable.Repeat("", 4 - s.MyClusters.Length)).ToArray();

                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Copy(tmp, FilePath, true);
                try { File.Delete(tmp); } catch { }
                Debug.WriteLine($"[Settings] Saved settings {FilePath} ({json.Length} bytes)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Settings] Save failed: {ex.Message}");
            }
        }

        public Color ColorForBand(string band)
        {
            if (BandColors != null && BandColors.TryGetValue(band, out var hex))
            {
                try { return ColorTranslator.FromHtml(hex); } catch { }
            }
            return Utils.ColorForBand(band);
        }

        public void SetBandColor(string band, Color c)
        {
            BandColors[band] = ColorTranslator.ToHtml(c);
        }

        // Helper to construct DX list font based on persisted settings
        public Font GetDxListFontOrDefault(Font fallback)
        {
            try
            {
                var ui = Ui;
                if (ui != null &&
                    !string.IsNullOrWhiteSpace(ui.DxListFontFamily) &&
                    ui.DxListFontSize.HasValue && ui.DxListFontSize.Value > 4f)
                {
                    return new Font(ui.DxListFontFamily!, ui.DxListFontSize!.Value, ui.DxListFontStyle, GraphicsUnit.Point);
                }
            }
            catch { }
            return fallback;
        }
    }
}
