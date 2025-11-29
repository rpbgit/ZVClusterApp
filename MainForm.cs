using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System.Text;

namespace ZVClusterApp.WinForms
{
    public partial class MainForm : Form
    {
        private readonly ClusterManager _clusterManager;
        private readonly RadioController _radio;
        private readonly AppSettings _settings;
        private readonly ConcurrentQueue<string> _rxLines = new();
        private CancellationTokenSource _cts = new();
        private Task? _pumpTask;
        private ComboBox _cmbClusters = null!;
        private Button _btnConnect = null!;
        private Label _lblStatus = null!;
        private Font? _dxListFont;

        // Runtime file logging handles
        private TextWriterTraceListener? _fileTraceListener;
        private StreamWriter? _fileLogWriter;

        // New: moved time/sun labels into MenuStrip
        private ToolStripLabel _menuUtc = null!;
        private ToolStripLabel _menuSunrise = null!;
        private ToolStripLabel _menuSunset = null!;

        private SplitContainer _split = null!;
        private TextBox _txtConsole = null!;
        private TextBox _txtConsoleInput = null!;
        private Button _btnSend = null!;

        private StatusStrip _status = null!;
        private ToolStripStatusLabel _stLocalServer = null!;
        private ToolStripStatusLabel _stCluster = null!; // kept

        private ToolStripStatusLabel _stUtc = null!;      // retained (not displayed) for code reuse
        private ToolStripStatusLabel _stSunrise = null!;  // retained (not displayed)
        private ToolStripStatusLabel _stSunset = null!;   // retained (not displayed)

        private System.Windows.Forms.Timer _clockTimer = null!;
        private System.Windows.Forms.Timer? _layoutRefreshTimer;

        private LocalClusterServer? _localServer;
        private CtyDat _cty = null!;
        private ListView _listView = null!;
        private readonly HashSet<string> _enabledBands = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ListViewItem> _allRows = new();
        private readonly List<ToolStripButton> _bandButtons = new();
        private string[] _knownBands = Array.Empty<string>();
        private readonly HashSet<string> _enabledModes = new(StringComparer.OrdinalIgnoreCase) { "CW", "SSB", "DAT" };
        private readonly List<ToolStripButton> _modeButtons = new();
        private bool _modeSolo = false;
        private HashSet<string> _preModeEnabled = new(StringComparer.OrdinalIgnoreCase);
        private string? _soloModeActive = null;
        private const string InputPrompt = "ClusterCMDHere> ";
        private bool _showUtc = true;
        private string _lastConnectedCluster = string.Empty;
        private const string _baseTitle = "ZV DX Cluster Monitor";
        private readonly List<ListViewItem> _fillerItems = new();
        private bool _soloMode = false;
        private HashSet<string> _preSoloEnabled = new(StringComparer.OrdinalIgnoreCase);
        private string? _soloBandActive = null;

        // Shortcut context menu for console input
        private ContextMenuStrip? _shortcutMenu;

        // NEW: Favorites
        private ToolStripButton _btnFavorites = null!;
        private bool _favoritesOnly = false;
        private readonly List<string> _favoritePatterns = new();
        private List<Regex> _favoriteRegexes = new();

        // === Helpers for Info column sizing ===
        private bool ListViewHasVScroll()
        {
            try
            {
                if (_listView.ClientSize.Height <= 0) return false;
                int rowH = GetRowHeight();
                if (rowH <= 0) return false;
                int visibleRows = (int)Math.Floor(_listView.ClientSize.Height / (double)rowH);
                int realCount = 0;
                for (int i = 0; i < _listView.Items.Count; i++) if (!Equals(_listView.Items[i].Tag, "__filler__")) realCount++;
                return realCount > visibleRows;
            }
            catch { return false; }
        }
        private void DeferUpdateInfoWidth() { try { if (IsHandleCreated) BeginInvoke((Action)UpdateInfoColumnWidth); } catch { } }
        // =====================================

        // ADD: timestamping trace listener (scoped to MainForm)
        private sealed class TimestampTraceListener : TextWriterTraceListener
        {
            public TimestampTraceListener(TextWriter writer, string name) : base(writer, name) { }
            private static string Stamp() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            public override void Write(string? message) => base.Write($"{Stamp()} {message}");
            public override void WriteLine(string? message) => base.WriteLine($"{Stamp()} {message}");
        }

        // ADD: dynamically add/remove file logging without restart (simplified: Trace only; Debug.Listeners not available)
        private void EnsureFileLogging(bool enable)
        {
            try
            {
                // Find existing listener by name
                TraceListener? existing = null;
                foreach (TraceListener l in Trace.Listeners)
                {
                    if (string.Equals(l.Name, "DebugLog", StringComparison.OrdinalIgnoreCase)) { existing = l; break; }
                }

                if (enable)
                {
                    if (existing != null) return; // already active
                    if (_fileTraceListener == null)
                    {
                        var logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                        _fileLogWriter = new StreamWriter(logPath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
                        _fileTraceListener = new TimestampTraceListener(_fileLogWriter, "DebugLog");
                        try { Trace.Listeners.Add(_fileTraceListener); } catch { }
                        try { Trace.WriteLine("==== Debug logging ENABLED (runtime) ===="); } catch { }
                    }
                }
                else
                {
                    if (existing != null)
                    {
                        try { Trace.Listeners.Remove(existing); } catch { }
                        try { existing.Flush(); existing.Close(); } catch { }
                    }
                    if (_fileTraceListener != null)
                    {
                        try { Trace.Listeners.Remove(_fileTraceListener); } catch { }
                        try { _fileTraceListener.Flush(); _fileTraceListener.Close(); } catch { }
                        _fileTraceListener = null;
                    }
                    try { _fileLogWriter?.Dispose(); } catch { }
                    _fileLogWriter = null;
                }
            }
            catch { }
        }

        public MainForm(ClusterManager clusterManager, RadioController radio, AppSettings settings)
        {
            _clusterManager = clusterManager; _radio = radio; _settings = settings;
            _lastConnectedCluster = _settings.LastConnectedCluster ?? string.Empty;
            Text = _baseTitle;
            if (_settings.Ui != null && _settings.Ui.Width > 0 && _settings.Ui.Height > 0) { Width = _settings.Ui.Width; Height = _settings.Ui.Height; }
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            InitializeUi();
            PopulateClustersCombo();
            EnsureCtyDatLoadedOnBoot();
            _clusterManager.LineReceived += Cluster_LineReceived;
            _pumpTask = Task.Run(() => ConsolePumpAsync(_cts.Token));
            _clockTimer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true }; _clockTimer.Tick += (s, e) => UpdateStatusBar(); UpdateStatusBar();
            _layoutRefreshTimer = new System.Windows.Forms.Timer { Interval = 100, Enabled = false }; _layoutRefreshTimer.Tick += (s, e) => { _layoutRefreshTimer!.Stop(); RepaintSpotsAfterLayout(); ReassertListFont(); SaveUiSettings(); };
            if (_settings.Ui?.SplitterDistance > 0) { try { _split.SplitterDistance = _settings.Ui.SplitterDistance; } catch { } }
            RestoreBandFiltersFromSettings(); RestoreModeFiltersFromSettings();

            // NEW: load favorites
            LoadFavoritesFromSettings();

            if (_settings.Ui != null)
            {
                try
                {
                    if (_settings.Ui.Left != 0 || _settings.Ui.Top != 0) { StartPosition = FormStartPosition.Manual; Left = _settings.Ui.Left; Top = _settings.Ui.Top; }
                    if (_settings.Ui.WindowState != 0) WindowState = (FormWindowState)_settings.Ui.WindowState;
                }
                catch { }
            }
            ResizeEnd += (s, e) => SaveUiSettings(); Move += (s, e) => SaveUiSettings(); SizeChanged += (s, e) => { if (WindowState == FormWindowState.Minimized) return; SaveUiSettings(); };
            Shown += MainForm_Shown; FormClosing += MainForm_FormClosing; _lastConnectedCluster = _settings.LastConnectedCluster ?? string.Empty;
        }

        private void InitializeUi()
        {
            // MenuStrip with standard menus
            var menu = new MenuStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            var miFile = new ToolStripMenuItem("File");
            var miView = new ToolStripMenuItem("View");
            var miHelp = new ToolStripMenuItem("Help");
            menu.Items.AddRange(new ToolStripItem[] { miFile, miView, miHelp });
            
            // File -> Exit
            var miExit = new ToolStripMenuItem("Exit", null, MiExit_Click) { ShortcutKeys = Keys.Alt | Keys.F4 };
            miFile.DropDownItems.Add(miExit);

            // Debug Log (checkable)
            var miDebugLog = new ToolStripMenuItem("Debug Log")
            {
                CheckOnClick = true,
                Checked = _settings.DebugLogEnabled,
                ToolTipText = "Toggle runtime debug logging (gates Debug/Trace writes to debug.log)."
            };
            miDebugLog.CheckedChanged += (s, e) =>
            {
                try
                {
                    _settings.DebugLogEnabled = miDebugLog.Checked;
                    AppSettings.Save(_settings);

                    EnsureFileLogging(miDebugLog.Checked); // ADD: actually add/remove the file listener now

                    AppendConsole($"[SYS] Debug logging {(miDebugLog.Checked ? "ENABLED" : "DISABLED")}.\r\n");
                    if (miDebugLog.Checked)
                    {
                        try { Debug.WriteLine("[DebugLog] Enabled at " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)); } catch { }
                    }
                }
                catch { }
            };
            miFile.DropDownItems.Add(new ToolStripSeparator());
            miFile.DropDownItems.Add(miDebugLog);

            // Add Settings under View menu
            var miSettings = new ToolStripMenuItem("Settings...", null, BtnSettings_Click);
            miView.DropDownItems.Add(miSettings);
            // Add Clusters editor
            var miClusters = new ToolStripMenuItem("Clusters...", null, MiClusters_Click) { ToolTipText = "Edit current cluster definition" };
            miView.DropDownItems.Add(miClusters);
            // Add DXP Calendar
            var miDxpCalendar = new ToolStripMenuItem("DXP Calendar", null, MiDxpCalendar_Click) { ToolTipText = "Open DXPedition calendar (DXNews / DX-World timeline)" };
            miView.DropDownItems.Add(miDxpCalendar);
            // Add Announced DXPs
            var miDxpsAnnounced = new ToolStripMenuItem("Announced DXP's", null, MiDxpsAnnounced_Click) { ToolTipText = "Open NG3K Announced DXpeditions" };
            miView.DropDownItems.Add(miDxpsAnnounced);
            // Add SolarHam
            var miSolarHam = new ToolStripMenuItem("SolarHam", null, MiSolarHam_Click) { ToolTipText = "Open SolarHam.com (solar conditions)" };
            miView.DropDownItems.Add(miSolarHam);
            // Add Contest Calendar
            var miContestCal = new ToolStripMenuItem("Contest Calendar", null, MiContestCalendar_Click) { ToolTipText = "Open weekly contest calendar" };
            miView.DropDownItems.Add(miContestCal);
            // About
            var miAbout = new ToolStripMenuItem("About...", null, MiAbout_Click) { ToolTipText = "Show version information" };
            miHelp.DropDownItems.Add(miAbout);

            // Create the four existing controls without absolute positioning
            _cmbClusters = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 1, Visible = false }; // hidden
            _btnConnect = new Button { Text = "Connect", Width = 90, Height = 24 };
            _lblStatus = new Label { Text = "Status: Disconnected", AutoSize = true };

            // Wire button events
            _btnConnect.Click += BtnConnect_Click;

            // Moved time/sun labels into menu (where cluster combo used to be)
            _menuUtc = new ToolStripLabel { Text = "UTC: --:--:--", IsLink = true, ToolTipText = "Click to toggle UTC/Local" };
            _menuSunrise = new ToolStripLabel { Text = "Sunrise: --:--" };
            _menuSunset = new ToolStripLabel { Text = "Sunset: --:--" };
            _menuUtc.Click += (s, e) => { _showUtc = !_showUtc; UpdateStatusBar(); };

            var hostStatus = new ToolStripControlHost(_lblStatus) { Alignment = ToolStripItemAlignment.Right, Margin = new Padding(8, 6, 4, 0) };
            var hostConnect = new ToolStripControlHost(_btnConnect) { Alignment = ToolStripItemAlignment.Right, Margin = new Padding(4, 2, 8, 2) };

            // Re-arrange: place time labels and connect/status as a right-aligned block.
            _menuUtc.Alignment = ToolStripItemAlignment.Right;
            _menuSunrise.Alignment = ToolStripItemAlignment.Right;
            _menuSunset.Alignment = ToolStripItemAlignment.Right;
            var sepTime = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };

            // Desired visual left-to-right order: Connect | Status | | UTC | Sunrise | Sunset
            // Add them in reverse (right-aligned insertion order).
            menu.Items.Add(_menuSunset);
            menu.Items.Add(_menuSunrise);
            menu.Items.Add(_menuUtc);
            menu.Items.Add(sepTime);
            menu.Items.Add(hostStatus);
            menu.Items.Add(hostConnect);
            menu.Items.Add(new ToolStripSeparator());

            MainMenuStrip = menu;

            // SplitContainer for console and list
            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
            _split.SplitterMoved += (s, e) => QueueRepaintSpots(); _split.SizeChanged += (s, e) => QueueRepaintSpots();

            // Console area (upper)
            _txtConsole = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                BackColor = Color.Black,
                ForeColor = Color.Lime
            };
            _txtConsole.Text = "Console ready.\r\n";

            var bottomConsolePanel = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            _txtConsoleInput = new TextBox { Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f) };
            _txtConsoleInput.KeyDown += TxtConsoleInput_KeyDown;
            _txtConsoleInput.KeyPress += TxtConsoleInput_KeyPress;
            _txtConsoleInput.TextChanged += TxtConsoleInput_TextChanged;
            _txtConsoleInput.MouseUp += (s, e) => EnsureCursorAfterPrompt();
            _txtConsoleInput.Enter += (s, e) => MoveCaretToEnd();
            _shortcutMenu = BuildShortcutMenu(_txtConsoleInput);
            _txtConsoleInput.ContextMenuStrip = _shortcutMenu;
            _btnSend = new Button { Text = "Send", Dock = DockStyle.Right, Width = 80 };
            _btnSend.Click += (s, e) => SendConsoleLine();
            bottomConsolePanel.Controls.Add(_txtConsoleInput);
            bottomConsolePanel.Controls.Add(_btnSend);

            var consolePanel = new Panel { Dock = DockStyle.Fill };
            consolePanel.Controls.Add(_txtConsole);
            consolePanel.Controls.Add(bottomConsolePanel);
            _split.Panel1.Controls.Add(consolePanel);

            // List view (lower)
            _listView = new FlickerFreeListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                Columns =
                {
                    new ColumnHeader { Text = "DX Call", Width = 100 },   // 0
                    new ColumnHeader { Text = "Freq", Width = 80 },       // 1
                    new ColumnHeader { Text = "Time", Width = 60 },       // 2
                    new ColumnHeader { Text = "Country", Width = 140 },   // 3
                    new ColumnHeader { Text = "Dist", Width = 65 },       // 4
                    new ColumnHeader { Text = "Bearing", Width = 75 },    // 5
                    new ColumnHeader { Text = "Spotter", Width = 80 },    // 6
                    new ColumnHeader { Text = "Info", Width = 200 }       // 7
                }
            };
            // Apply persisted font for DX list (cached + guarded)
            InitializeDxListFont();
            // Re-assert font when handle recreates (e.g., after layout/theme changes)
            _listView.HandleCreated += (s, e) => { try { ApplyListFontFromSettings(); } catch { } };

            _listView.Resize += (s, e) => { UpdateInfoColumnWidth(); ScrollSpotsToBottom(); };
            _listView.Scrollable = true;

            var ctx = new ContextMenuStrip();
            var miJump = new ToolStripMenuItem("Jump Radio", null, (s, e) => Context_JumpRadio());
            var miQrzDx = new ToolStripMenuItem("QRZ Lookup - DX", null, (s, e) => Context_QrzLookup(false));
            var miQrzSpotter = new ToolStripMenuItem("QRZ Lookup - Spotter", null, (s, e) => Context_QrzLookup(true));
            var miDxNews = new ToolStripMenuItem("DXNews Lookup - DX", null, (s, e) => Context_DxNews());
            var miRbnDx = new ToolStripMenuItem("RBN Lookup - DX", null, (s, e) => Context_RbnLookupDx());
            ctx.Items.AddRange(new ToolStripItem[] { miJump, miQrzDx, miQrzSpotter, miDxNews, miRbnDx });
            ctx.Opening += (s, e) =>
            {
                bool enableRbn = false;
                try
                {
                    if (_listView.SelectedItems.Count > 0)
                    {
                        var itSel = _listView.SelectedItems[0];
                        int hz = ParseFrequencyToHz(itSel.SubItems.Count > 1 ? (itSel.SubItems[1].Text ?? string.Empty) : string.Empty);
                        enableRbn = hz <= 0 || !IsInSsbSubband(hz);
                    }
                }
                catch { enableRbn = false; }
                miRbnDx.Enabled = enableRbn;
            };
            _listView.ContextMenuStrip = ctx;
            _split.Panel2.Controls.Add(_listView);

            // Status strip
            _status = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                ShowItemToolTips = true,
                Items = { new ToolStripStatusLabel { Spring = true }, (_stLocalServer = new ToolStripStatusLabel { Text = "Server: 0 clients" }), new ToolStripStatusLabel { Text = " | " }, (_stCluster = new ToolStripStatusLabel { Text = "Cluster: (none)" }) }
            };
            _stUtc = new ToolStripStatusLabel(); _stSunrise = new ToolStripStatusLabel(); _stSunset = new ToolStripStatusLabel();

            var lblModes = new ToolStripStatusLabel { Text = " | Modes:" }; _status.Items.Add(lblModes);
            foreach (var mode in new[] { "CW", "SSB", "DAT" })
            {
                var mb = new ToolStripButton(mode) { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = $"Toggle {mode} spots" };
                mb.Click += (s, e) => ModeButtonClicked(mode, (ToolStripButton)s!);
                mb.ToolTipText = $"{mode}: Click toggles mode. Ctrl+Click = Solo/restore. Shift+Click = Enable all modes.";
                _modeButtons.Add(mb); _status.Items.Add(mb);
            }

            // NEW: Favorites button (before bands)
            _btnFavorites = new ToolStripButton("Fav")
            {
                CheckOnClick = true,
                Checked = false,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "DX Favorites filter: show only matching DX calls. Right-click to edit list."
            };
            _btnFavorites.Click += (s, e) => ToggleFavoritesFilter(_btnFavorites.Checked);
            _btnFavorites.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) OpenFavoritesEditor(); };
            //_status.Items.Add(new ToolStripStatusLabel { Text = " | Fav:" });
            _status.Items.Add(_btnFavorites);

            _knownBands = (_settings.TrackBands != null && _settings.TrackBands.Length > 0) ? _settings.TrackBands : new[] { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m" };
            _enabledBands.Clear();
            foreach (var bnd in _knownBands)
            {
                _enabledBands.Add(bnd);
                var color = _settings.ColorForBand(bnd);
                var btn = new ToolStripButton(bnd) { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text, ForeColor = color, ToolTipText = $"Toggle {bnd} spots" };
                btn.Click += (s, e) => BandButtonClicked(bnd, (ToolStripButton)s!);
                btn.ToolTipText = $"{bnd}: Click toggles band. Ctrl+Click = Solo/restore. Shift+Click = Enable all bands.";
                _bandButtons.Add(btn); _status.Items.Add(btn);
            }
            try { _status.RenderMode = ToolStripRenderMode.Professional; _status.Renderer = new BandColorRenderer(_settings, new HashSet<string>(_knownBands, StringComparer.OrdinalIgnoreCase), new HashSet<string>(new[] { "CW", "SSB", "DAT" }, StringComparer.OrdinalIgnoreCase)); } catch { }

            Controls.Clear(); Controls.Add(_split); Controls.Add(_status); Controls.Add(menu);

            _txtConsoleInput.Text = InputPrompt;
            MoveCaretToEnd();
            UpdateClusterIndicator();
            UpdateFavoritesButtonVisual();

            // ADD: align listeners with current setting on startup (ensures Debug.WriteLine goes to file only when enabled)
            EnsureFileLogging(_settings.DebugLogEnabled);
        }

        // Menu item handlers (implemented)
        private void MiDxpCalendar_Click(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.hamradiotimeline.com/timeline/dxw_timeline_1_1.php",
                    UseShellExecute = true
                });
            }
            catch
            {
                try { MessageBox.Show(this, "Unable to open DXP Calendar URL.", "Open Failed", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
        }

        private void MiDxpsAnnounced_Click(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.ng3k.com/Misc/adxo.html",
                    UseShellExecute = true
                });
            }
            catch
            {
                try { MessageBox.Show(this, "Unable to open NG3K Announced DXpeditions URL.", "Open Failed", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
        }

        private void MiSolarHam_Click(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://solarham.com/",
                    UseShellExecute = true
                });
            }
            catch
            {
                try { MessageBox.Show(this, "Unable to open SolarHam URL.", "Open Failed", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
        }

        private void MiContestCalendar_Click(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.contestcalendar.com/weeklycont.php",
                    UseShellExecute = true
                });
            }
            catch
            {
                try { MessageBox.Show(this, "Unable to open Contest Calendar URL.", "Open Failed", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
        }

        private void MiAbout_Click(object? sender, EventArgs e)
        {
            try
            {
                static string NormalizeVer(string? v)
                {
                    if (string.IsNullOrWhiteSpace(v)) return string.Empty;
                    var s = v.Trim();
                    var plus = s.IndexOf('+');
                    if (plus >= 0) s = s.Substring(0, plus); // drop build metadata like +githash
                    s = Regex.Replace(s, @"\s*\(.*\)$", string.Empty); // drop trailing parens metadata
                    return s;
                }

                var entry = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var infoVerRaw = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                string? productVerRaw = null;
                string? fileVerRaw = null;
                try
                {
                    var exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                        exePath = Application.ExecutablePath;

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        var fvi = FileVersionInfo.GetVersionInfo(exePath);
                        productVerRaw = fvi.ProductVersion;
                        fileVerRaw = fvi.FileVersion;
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
                if (string.IsNullOrEmpty(infoVer)) infoVer = "(unknown)";

                MessageBox.Show(this,
                    $"ZV DX Cluster Monitor\nVersion: {infoVer}",
                    "About",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch
            {
                try { MessageBox.Show(this, "Version information unavailable.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            }
        }

        // NEW: Favorites helpers

        private void LoadFavoritesFromSettings()
        {
            try
            {
                _favoritePatterns.Clear();
                if (_settings.FavoriteDxCalls != null)
                    _favoritePatterns.AddRange(_settings.FavoriteDxCalls.Select(p => (p ?? string.Empty).Trim()).Where(p => p.Length > 0));
                CompileFavoriteRegexes();
            }
            catch { }
        }

        private void CompileFavoriteRegexes()
        {
            var list = new List<Regex>();
            foreach (var pat in _favoritePatterns)
            {
                try
                {
                    // map * -> .*, ? -> .
                    var rx = "^" + Regex.Escape(pat).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    list.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }
                catch { }
            }
            _favoriteRegexes = list;
        }

        private void ToggleFavoritesFilter(bool on)
        {
            _favoritesOnly = on;
            UpdateFavoritesButtonVisual();
            ApplyBandFilter();
        }

        private void UpdateFavoritesButtonVisual()
        {
            try
            {
                if (_btnFavorites == null) return;
                if (_favoritesOnly)
                {
                    _btnFavorites.Text = "DX*";
                    _btnFavorites.BackColor = Color.Gold;
                    _btnFavorites.ForeColor = Color.Black;
                }     
                else
                {
                    _btnFavorites.Text = "DX";
                    _btnFavorites.BackColor = SystemColors.Control;
                    _btnFavorites.ForeColor = SystemColors.ControlText;
                }
            }
            catch { }
        }

        private void OpenFavoritesEditor()
        {
            try
            {
                // Use the same font as the DX list but pass a CLONE so the dialog owns its own instance.
                var edFont = (_listView.Font != null)
                    ? new Font(_listView.Font, _listView.Font.Style)
                    : AppSettings.CreateDefaultDxListFont();

                using var dlg = new FavoritesEditForm(_favoritePatterns, edFont);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _favoritePatterns.Clear();
                    _favoritePatterns.AddRange(dlg.Patterns);
                    CompileFavoriteRegexes();
                    _settings.FavoriteDxCalls = _favoritePatterns.ToList();
                    AppSettings.Save(_settings);
                    ApplyBandFilter();
                }
            }
            catch { }
            UpdateFavoritesButtonVisual();
        }

        private bool IsFavoriteCall(string dxCall)
        {
            if (_favoriteRegexes == null || _favoriteRegexes.Count == 0) return false;
            if (string.IsNullOrWhiteSpace(dxCall)) return false;
            foreach (var rx in _favoriteRegexes)
            {
                try { if (rx.IsMatch(dxCall)) return true; } catch { }
            }
            return false;
        }

        private bool FavoritesCondition(ListViewItem it)
        {
            if (!_favoritesOnly) return true;
            try
            {
                var dx = it.SubItems.Count > 0 ? (it.SubItems[0].Text ?? string.Empty) : string.Empty;
                dx = dx.Trim().ToUpperInvariant();
                return IsFavoriteCall(dx);
            }
            catch { return false; }
        }

        // === Helpers for layout repaint and fillers ===
        private void QueueRepaintSpots()
        {
            try
            {
                if (_layoutRefreshTimer != null) { _layoutRefreshTimer.Stop(); _layoutRefreshTimer.Start(); }
            }
            catch { }
        }

        private void RepaintSpotsAfterLayout()
        {
            try
            {
                if (_listView == null || _listView.IsDisposed) return;
                UiUtil.Suspend(_listView);
                _listView.BeginUpdate();
                FillListToBottom();
                UpdateInfoColumnWidth(); // recompute during layout
                _listView.EndUpdate();
                UiUtil.Resume(_listView);
                DeferUpdateInfoWidth(); // deferred pass after layout settles
                ScrollSpotsToBottom();
            }
            catch { }
        }

        private int GetRowHeight()
        {
            try
            {
                int h = _listView.Font.Height + 6;
                if (_listView.Items.Count > 0)
                {
                    var r = _listView.GetItemRect(Math.Min(0, _listView.Items.Count - 1));
                    if (r.Height > 0) h = r.Height;
                }
                return Math.Max(18, h);
            }
            catch { return 18; }
        }

        private void FillListToBottom()
        {
            try
            {
                if (_listView.ClientSize.Height <= 0) return;
                int rowH = GetRowHeight();
                if (rowH <= 0) return;
                int visibleRows = (int)Math.Ceiling(_listView.ClientSize.Height / (double)rowH);

                int currentFillers = 0;
                for (int i = _listView.Items.Count - 1; i >= 0; i--)
                {
                    if (Equals(_listView.Items[i].Tag, "__filler__")) currentFillers++;
                    else break;
                }

                int realCount = _listView.Items.Count - currentFillers;
                int neededFillers = Math.Max(0, visibleRows - realCount);

                if (neededFillers == currentFillers) return;

                if (neededFillers < currentFillers)
                {
                    int toRemove = currentFillers - neededFillers;
                    while (toRemove-- > 0 && _listView.Items.Count > 0)
                    {
                        int idx = _listView.Items.Count - 1;
                        if (Equals(_listView.Items[idx].Tag, "__filler__")) _listView.Items.RemoveAt(idx);
                        else break;
                    }
                }
                else
                {
                    int toAdd = neededFillers - currentFillers;
                    for (int i = 0; i < toAdd; i++)
                    {
                        var filler = new ListViewItem(string.Empty) { Tag = "__filler__" };
                        for (int c = 1; c < _listView.Columns.Count; c++) filler.SubItems.Add(string.Empty);
                        filler.ForeColor = SystemColors.ControlText;
                        filler.BackColor = _listView.BackColor;
                        _listView.Items.Add(filler);
                    }
                }
            }
            catch { }
        }

        private void AddListViewRawLine(string raw)
        {
            if (InvokeRequired) { try { BeginInvoke((Action)(() => AddListViewRawLine(raw))); } catch { } return; }
            try
            {
                // Strip leading cluster tag like "[AI9T] " if present
                string line = raw;
                if (line.Length > 2 && line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    if (end > 0 && end + 1 < line.Length)
                    {
                        int start = end + 1;
                        if (start < line.Length && line[start] == ' ') start++;
                        line = line.Substring(start);
                    }
                }

                // Normalize spaces
                line = Regex.Replace(line, "\\s+", " ").Trim();

                // Ignore general broadcast messages starting with "To ALL de" (skip country/spot parsing)
                if (Regex.IsMatch(line, @"^TO\s+ALL\s+DE\s+", RegexOptions.IgnoreCase))
                {
                    return; // do not attempt country (CTY) match or treat as a DX spot
                }

                // Guard: ignore directed messages addressed to me, like "MYCALL de SPOTTER ..."

                try
                {
                    var myCall = (_settings.MyCall ?? string.Empty).Trim().ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(myCall))
                    {
                        var up = line.ToUpperInvariant();
                        if (up.StartsWith(myCall + " DE ")) return;
                    }
                }
                catch { }

                string spotter = string.Empty;
                string freqTxt = string.Empty;
                string dxCall = string.Empty;
                string info = string.Empty;

                // helper to strip dates/times from info
                static string StripDateTimeTokens(string s)
                {
                    if (string.IsNullOrEmpty(s)) return s;
                    // Remove dates like 05-Nov-2025, 5-Nov-25, 05/Nov/2025
                    s = Regex.Replace(s, @"\b\d{1,2}[-/](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[A-Za-z]*[-/]\d{2,4}\b", string.Empty, RegexOptions.IgnoreCase);
                    // Remove times like 2137Z or 0730Z
                    s = Regex.Replace(s, @"\b\d{3,4}Z\b", string.Empty, RegexOptions.IgnoreCase);
                    // Collapse spaces
                    s = Regex.Replace(s, "\\s+", " ").Trim();
                    return s;
                }

                // helper to extract <CALL> as spotter and remove it from text
                static string ExtractAngleSpotter(string text, ref string spotterOut)
                {
                    if (string.IsNullOrEmpty(text)) return text;
                    var m = Regex.Match(text, @"<\s*(?<call>[A-Z0-9][A-Z0-9/#-]*)\s?>", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var call = m.Groups["call"].Value;
                        if (!string.IsNullOrWhiteSpace(call)) spotterOut = call;
                        text = text.Remove(m.Index, m.Length);
                        text = Regex.Replace(text, "\\s+", " ").Trim();
                    }
                    return text;
                }

                // Try multiple common formats (relaxed callsign pattern)
                // 1) DX de <spotter>[:]? <freq> <dxcall> ...
                var m1 = Regex.Match(line,
                    @"DX\s+de\s+(?<sp>[A-Z0-9][A-Z0-9/#-]*)\s*:?[ ]+(?<fq>\d{3,}(?:[\.,]\d{1,3})?)(?:\s*(?:kHz|Hz|MHZ|MHz))?\s+(?<dx>[A-Z0-9][A-Z0-9/]{1,})(?<rest>.*)",
                    RegexOptions.IgnoreCase);

                // 2) <freq> <dxcall> ... DX de <spotter>
                var m2 = !m1.Success ? Regex.Match(line,
                    @"(?<fq>\d{3,}(?:[\.,]\d{1,3})?)(?:\s*(?:kHz|Hz|MHZ|MHz))?\s+(?<dx>[A-Z0-9][A-Z0-9/]{1,}).*?DX\s+de\s+(?<sp>[A-Z0-9][A-Z0-9/#-]*)\b(?<rest>.*)",
                    RegexOptions.IgnoreCase) : Match.Empty;

                // 3) DXSpider sh/dx style: <freq> <dxcall> [mode ...] <time>Z <spotter(-#)
                var m3 = (!m1.Success && !m2.Success) ? Regex.Match(line,
                    @"^(?<fq>\d{3,}(?:[\.,]\d{1,3})?)\s+(?<dx>[A-Z0-9][A-Z0-9/]{1,})\s+(?<rest>.+)$",
                    RegexOptions.IgnoreCase) : Match.Empty;

                if (m1.Success || m2.Success)
                {
                    var m = m1.Success ? m1 : m2;
                    spotter = m.Groups["sp"].Value.Trim();
                    freqTxt = m.Groups["fq"].Value.Trim();
                    dxCall = m.Groups["dx"].Value.Trim();
                    info = (m.Groups["rest"].Success ? m.Groups["rest"].Value : string.Empty).Trim();
                    info = ExtractAngleSpotter(info, ref spotter);
                    info = StripDateTimeTokens(info);
                }
                else if (m3.Success)
                {
                    freqTxt = m3.Groups["fq"].Value.Trim();
                    dxCall = m3.Groups["dx"].Value.Trim();
                    var rest = m3.Groups["rest"].Value.Trim();

                    // Try angle-bracket spotter first
                    rest = ExtractAngleSpotter(rest, ref spotter);

                    // Try time + spotter at end: e.g., "... 1234Z W3LPL-5"
                    var mt = Regex.Match(rest, @"(?<pre>.*?)(?<tm>\b\d{3,4}Z\b)\s+(?<sp>[A-Z0-9][A-Z0-9/#-]*)$", RegexOptions.IgnoreCase);
                    if (mt.Success)
                    {
                        if (string.IsNullOrWhiteSpace(spotter)) spotter = mt.Groups["sp"].Value.Trim();
                        info = mt.Groups["pre"].Value.Trim();
                    }
                    else
                    {
                        // If no time token, try to use last token as spotter if it looks like a call
                        var tokens = rest.Split(' ');
                        if (tokens.Length > 0)
                        {
                            var lastTok = tokens[^1];
                            if (Regex.IsMatch(lastTok, @"^[A-Z0-9][A-Z0-9/]*-?\d*$", RegexOptions.IgnoreCase) && string.IsNullOrWhiteSpace(spotter))
                            {
                                spotter = lastTok;
                                info = string.Join(' ', tokens.Take(tokens.Length - 1)).Trim();
                            }
                            else
                            {
                                info = rest;
                            }
                        }
                        else info = rest;
                    }
                    info = StripDateTimeTokens(info);
                }
                else
                {
                    // Generic fallback: frequency followed by dx call; spotter optional
                    var mf = Regex.Match(line, @"(?<fq>\d{3,}(?:[\.,]\d{1,3})?)");
                    if (!mf.Success) return;
                    freqTxt = mf.Groups["fq"].Value.Trim();

                    // dx call as next token after freq
                    int idxAfterFreq = line.IndexOf(freqTxt, StringComparison.Ordinal);
                    if (idxAfterFreq >= 0)
                    {
                        var tail = line[(idxAfterFreq + freqTxt.Length)..].Trim();
                        tail = ExtractAngleSpotter(tail, ref spotter);
                        var tokens = tail.Split(' ');
                        if (tokens.Length > 0)
                        {
                            var candidate = Regex.Replace(tokens[0], @"[^A-Z0-9/]+", string.Empty, RegexOptions.IgnoreCase);
                            dxCall = candidate;
                            // Try to find spotter pattern at the end
                            if (tokens.Length > 1)
                            {
                                var last = tokens[^1];
                                if (Regex.IsMatch(last, @"^[A-Z0-9][A-Z0-9/]*-?\d*$", RegexOptions.IgnoreCase) && string.IsNullOrWhiteSpace(spotter))
                                {
                                    spotter = last;
                                    info = string.Join(' ', tokens.Take(Math.Max(0, tokens.Length - 1))).Trim();
                                }
                                else info = string.Join(' ', tokens.Skip(1)).Trim();
                            }
                        }
                    }
                    info = StripDateTimeTokens(info);
                }

                if (string.IsNullOrWhiteSpace(freqTxt) || string.IsNullOrWhiteSpace(dxCall)) return;

                // Clean frequency token and normalize decimal comma
                freqTxt = freqTxt.Replace(',', '.');
                freqTxt = Regex.Replace(freqTxt, "[^0-9.]", string.Empty);

                int hz = ParseFrequencyToHz(freqTxt);
                string band = hz > 0 ? Utils.FrequencyToBand(hz) : string.Empty;

                // Sanity-check frequency range (avoid parsing dates/times as spots)
                if (hz < 1_800_000 || hz > 54_000_000) return;

                var it = new ListViewItem(dxCall);
                it.SubItems.Add(freqTxt);                               // 1 Freq
                it.SubItems.Add(DateTime.UtcNow.ToString("HH:mm:ss"));  // 2 Time
                it.SubItems.Add(string.Empty);                          // 3 Country
                it.SubItems.Add(string.Empty);                          // 4 Dist
                it.SubItems.Add(string.Empty);                          // 5 Bearing
                it.SubItems.Add(spotter);                               // 6 Spotter
                it.SubItems.Add(info);                                  // 7 Info
                it.Tag = band;
                // Ensure item has no individual Font so it inherits ListView.Font
                it.UseItemStyleForSubItems = true;
                // Apply per-band colors immediately so new rows are colored without waiting for a filter refresh
                if (!string.IsNullOrWhiteSpace(band))
                {
                    var baseColor = _settings.ColorForBand(band);
                    var brightness = baseColor.GetBrightness();
                    var fore = brightness < 0.5f ? Color.White : Color.Black;
                    it.BackColor = baseColor;
                    it.ForeColor = fore;
                    foreach (ListViewItem.ListViewSubItem si in it.SubItems)
                    {
                        si.BackColor = baseColor;
                        si.ForeColor = fore;
                    }
                }

                // Country and geo if CTY available and user location set
                try
                {
                    var entry = _cty?.Lookup(dxCall);
                    if (entry != null && Utils.TryGridToLatLon(_settings.GridSquare ?? string.Empty, out var myLat, out var myLon))
                    {
                        var km = Utils.GcDistanceKm(myLat, myLon, entry.Lat, entry.Lon);
                        var miles = Utils.KmToMilesRounded(km);
                        var distText = _settings.UseKilometers ? $"{km:F0} km" : $"{miles} mi";
                        var brg = Utils.GcInitialBearing(myLat, myLon, entry.Lat, entry.Lon);
                        var brgLong = Utils.Normalize360(brg + 180.0);
                        it.SubItems[3].Text = entry.Country;            // Country index moved from 4 -> 3
                        it.SubItems[4].Text = distText;                 // Dist stays at 4
                        it.SubItems[5].Text = $"{Math.Round(brg)}° / {Math.Round(brgLong)}°"; // Bearing index moved from 6 -> 5
                    }
                }
                catch { }

                _allRows.Add(it);
                if (_allRows.Count > 5000) _allRows.RemoveRange(0, _allRows.Count - 5000);

                if (ItemBandIsEnabled(it) && ItemModeIsEnabled(it) && FavoritesCondition(it))
                {
                    UiUtil.Suspend(_listView);
                    _listView.BeginUpdate();

                    // Insert before fillers (so fillers stay at end)
                    int insertIndex = _listView.Items.Count;
                    // if last items are fillers, insert before them
                    while (insertIndex > 0 && Equals(_listView.Items[insertIndex - 1].Tag, "__filler__")) insertIndex--;
                    if (insertIndex == _listView.Items.Count) _listView.Items.Add(it);
                    else _listView.Items.Insert(insertIndex, it);

                    // Prune oldest real rows beyond 1000
                    int realCount = _listView.Items.Cast<ListViewItem>().Count(x => !Equals(x.Tag, "__filler__"));
                    while (realCount > 1000 && _listView.Items.Count > 0)
                    {
                        if (!Equals(_listView.Items[0].Tag, "__filler__")) { _listView.Items.RemoveAt(0); realCount--; }
                        else break; // should not happen since fillers are at end
                    }

                    FillListToBottom();
                    UpdateInfoColumnWidth(); // tighten after adding
                    _listView.EndUpdate();
                    UiUtil.Resume(_listView);
                    DeferUpdateInfoWidth(); // deferred tighten (handles scrollbar appearing)
                    ScrollSpotsToBottom();
                }
            }
            catch { }
        }

        private static int ParseFrequencyToHz(string freqTxt)
        {
            if (string.IsNullOrWhiteSpace(freqTxt)) return 0;
            // Strip non-numeric except dot
            var cleaned = Regex.Replace(freqTxt, "[^0-9.]", string.Empty);
            if (string.IsNullOrWhiteSpace(cleaned)) return 0;
            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
            if (v >= 100000) return (int)Math.Round(v);            // assume Hz
            if (v >= 1000) return (int)Math.Round(v * 1000.0);     // assume kHz
            return (int)Math.Round(v * 1_000_000.0);               // assume MHz
        }

        private string GetItemBand(ListViewItem it)
        {
            try
            {
                var tag = it.Tag as string;
                if (!string.IsNullOrWhiteSpace(tag)) return tag;
                // derive from frequency subitem if available
                if (it.SubItems != null && it.SubItems.Count > 1)
                {
                    var ftxt = it.SubItems[1].Text ?? string.Empty;
                    ftxt = ftxt.Replace(',', '.');
                    ftxt = Regex.Replace(ftxt, "[^0-9.]", string.Empty);
                    int hz = ParseFrequencyToHz(ftxt);
                    if (hz > 0)
                    {
                        var b = Utils.FrequencyToBand(hz);
                        // normalize to known set if it's rendered like "50MHz"
                        if (b.EndsWith("MHz", StringComparison.OrdinalIgnoreCase))
                        {
                            // Map MHz text back to nearest canonical band if within ranges
                            int khz = hz / 1000;
                            if (khz >= 1800 && khz <= 2000) b = "160m";
                            else if (khz >= 3500 && khz <= 4000) b = "80m";
                            else if (khz >= 5330 && khz <= 5406) b = "60m";
                            else if (khz >= 7000 && khz <= 7300) b = "40m";
                            else if (khz >= 10100 && khz <= 10150) b = "30m";
                            else if (khz >= 14000 && khz <= 14350) b = "20m";
                            else if (khz >= 18068 && khz <= 18168) b = "17m";
                            else if (khz >= 21000 && khz <= 21450) b = "15m";
                            else if (khz >= 24890 && khz <= 24990) b = "12m";
                            else if (khz >= 28000 && khz <= 29700) b = "10m";
                            else if (khz >= 50000 && khz <= 54000) b = "6m";
                        }
                        return b;
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private bool ItemBandIsEnabled(ListViewItem it)
        {
            var band = GetItemBand(it);
            if (string.IsNullOrWhiteSpace(band)) return true; // show unrecognized
            return _enabledBands.Contains(band);
        }

        private bool ItemModeIsEnabled(ListViewItem it)
        {
            var mode = InferModeForSpot(it);
            return _enabledModes.Contains(mode);
        }

        // Toggle handlers (added)
        private void ModeButtonClicked(string mode, ToolStripButton b)
        {
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control; bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            if (ctrl)
            {
                if (!_modeSolo) { _modeSolo = true; _soloModeActive = mode; _preModeEnabled = new HashSet<string>(_enabledModes, StringComparer.OrdinalIgnoreCase); _enabledModes.Clear(); _enabledModes.Add(mode); UpdateModeButtonsFromEnabled(); ApplyBandFilter(); }
                else if (!string.IsNullOrEmpty(_soloModeActive) && string.Equals(_soloModeActive, mode, StringComparison.OrdinalIgnoreCase)) { _modeSolo = false; _soloModeActive = null; _enabledModes.Clear(); foreach (var m in _preModeEnabled) _enabledModes.Add(m); UpdateModeButtonsFromEnabled(); ApplyBandFilter(); }
                else { _soloModeActive = mode; _enabledModes.Clear(); _enabledModes.Add(mode); UpdateModeButtonsFromEnabled(); ApplyBandFilter(); }
                return;
            }
            if (shift && !ctrl)
            {
                // SHIFT+Click: enable all modes (clear solo state)
                _modeSolo = false; _soloModeActive = null; _enabledModes.Clear();
                foreach (var mb in _modeButtons) if (!string.IsNullOrWhiteSpace(mb.Text)) _enabledModes.Add(mb.Text);
                UpdateModeButtonsFromEnabled(); ApplyBandFilter();
                return;
            }
            ToggleModeFilter(mode, b.Checked);
        }

        private void BandButtonClicked(string bandName, ToolStripButton tsb)
        {
            bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control; bool shift = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            if (ctrl)
            {
                if (!_soloMode) { _soloMode = true; _soloBandActive = bandName; _preSoloEnabled = new HashSet<string>(_enabledBands, StringComparer.OrdinalIgnoreCase); _enabledBands.Clear(); _enabledBands.Add(bandName); UpdateBandButtonsFromEnabled(); ApplyBandFilter(); }
                else if (!string.IsNullOrEmpty(_soloBandActive) && string.Equals(_soloBandActive, bandName, StringComparison.OrdinalIgnoreCase)) { _soloMode = false; _soloBandActive = null; _enabledBands.Clear(); foreach (var be in _preSoloEnabled) _enabledBands.Add(be); UpdateBandButtonsFromEnabled(); ApplyBandFilter(); }
                else { _soloBandActive = bandName; _enabledBands.Clear(); _enabledBands.Add(bandName); UpdateBandButtonsFromEnabled(); ApplyBandFilter(); }
                return;
            }
            if (shift && !ctrl)
            {
                // SHIFT+Click: enable all bands (clear solo state)
                _soloMode = false; _soloBandActive = null; _enabledBands.Clear();
                foreach (var bnd in _knownBands) _enabledBands.Add(bnd);
                UpdateBandButtonsFromEnabled(); ApplyBandFilter();
                return;
            }
            ToggleBandFilter(bandName, tsb.Checked);
        }

        private void ToggleBandFilter(string band, bool enabled)
        {
            if (enabled)
            {
                _enabledBands.Add(band);
            }
            else
            {
                _enabledBands.Remove(band);
            }
            ApplyBandFilter();
        }

        private void ToggleModeFilter(string mode, bool enabled)
        {
            if (enabled) _enabledModes.Add(mode); else _enabledModes.Remove(mode);
            ApplyBandFilter();
        }

        private static string FormatFreqHz(int hz)
        {
            try
            {
                if (hz >= 1_000_000) return (hz / 1_000_000.0).ToString("0.000", CultureInfo.InvariantCulture) + " MHz";
                if (hz >= 1_000) return (hz / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " kHz";
                return hz.ToString(CultureInfo.InvariantCulture) + " Hz";
            }
            catch { return hz.ToString(CultureInfo.InvariantCulture) + " Hz"; }
        }

        private void ApplyBandFilter()
        {
            try
            {
                if (_listView.IsDisposed) return;
                UiUtil.Suspend(_listView);
                _listView.BeginUpdate();
                _listView.Items.Clear();
                var filtered = _allRows.Where(it => ItemBandIsEnabled(it) && ItemModeIsEnabled(it) && FavoritesCondition(it)).TakeLast(1000);
                foreach (var src in filtered)
                {
                    // Rebuild item without cloning per-item Font/handle state to avoid font resets
                    var clone = new ListViewItem(src.Text ?? string.Empty);
                    for (int i = 1; i < src.SubItems.Count; i++)
                    {
                        var txt = src.SubItems[i].Text ?? string.Empty;
                        clone.SubItems.Add(txt);
                    }
                    // src subitem indices already follow new layout: 0 DX,1 Freq,2 Time,3 Country,4 Dist,5 Bearing,6 Spotter,7 Info
                    var band = GetItemBand(src);
                    clone.Tag = band;
                    // Ensure item has no individual Font so it inherits ListView.Font
                    clone.UseItemStyleForSubItems = true;
                    // Apply per-band colors immediately so new rows are colored without waiting for a filter refresh
                    if (!string.IsNullOrWhiteSpace(band))
                    {
                        var baseColor = _settings.ColorForBand(band);
                        var brightness = baseColor.GetBrightness();
                        var fore = brightness < 0.5f ? Color.White : Color.Black;
                        clone.BackColor = baseColor;
                        clone.ForeColor = fore;
                        foreach (ListViewItem.ListViewSubItem si in clone.SubItems)
                        {
                            si.BackColor = baseColor;
                            si.ForeColor = fore;
                        }
                    }
                    _listView.Items.Add(clone);
                }
                FillListToBottom();
                UpdateInfoColumnWidth(); // immediate sizing
                _listView.EndUpdate();

                UiUtil.Resume(_listView);

                // Re-assert the configured DX list font after layout has resumed to avoid transient fallback
                try { ApplyListFontFromSettings(); } catch { }

                UiUtil.Resume(_listView);
                DeferUpdateInfoWidth(); // deferred sizing (handles scrollbar appearance changes)
            }
            catch { }
            finally { ScrollSpotsToBottom(); }
        }

        private void AppendConsole(string text)
        {
            if (InvokeRequired) { try { BeginInvoke((Action)(() => AppendConsole(text))); } catch { } return; }
            try
            {
                _txtConsole.AppendText(text);
                // Keep last ~10k chars
                const int max = 10000;
                if (_txtConsole.TextLength > max)
                {
                    _txtConsole.SelectionStart = 0;
                    _txtConsole.SelectionLength = _txtConsole.TextLength - max;
                    _txtConsole.SelectedText = string.Empty;
                }
                // Always move caret to end and ensure view is scrolled to last line
                _txtConsole.SelectionStart = _txtConsole.TextLength;
                _txtConsole.SelectionLength = 0;
                _txtConsole.ScrollToCaret();
            }
            catch { }
        }

        private void UpdateLocalServerIndicator(int count)
        {
            if (InvokeRequired) { try { BeginInvoke((Action)(() => UpdateLocalServerIndicator(count))); } catch { } return; }
            try
            {
                var text = count == 0 ? "Server: no client" : (count == 1 ? "Server: 1 client" : $"Server: {count} clients");
                _stLocalServer.Text = text;
                _stLocalServer.ForeColor = count > 0 ? Color.LimeGreen : SystemColors.ControlText;
            }
            catch { }
        }

        private void UpdateStatusBar()
        {
            try
            {
                var nowUtc = DateTime.UtcNow; var offset = TimeSpan.FromHours(_settings.GmtOffsetHours); var nowDisp = _showUtc ? nowUtc : nowUtc + offset;
                var (sr, ss) = ComputeSunriseSunsetUtc(nowUtc.Date, _settings.GridSquare ?? string.Empty);
                string fmt(DateTime? tUtc) { if (!tUtc.HasValue) return "n/a"; var t = _showUtc ? tUtc.Value : tUtc.Value + offset; return t.ToString("HH:mm", CultureInfo.InvariantCulture); }
                string utcText = (_showUtc ? "UTC" : "Local") + $": {nowDisp:HH:mm:ss}";
                string sunriseText = "Sunrise: " + fmt(sr);
                string sunsetText = "Sunset: " + fmt(ss);
                // Update menu labels (primary)
                _menuUtc.Text = utcText; _menuSunrise.Text = sunriseText; _menuSunset.Text = sunsetText;
                // Keep hidden status labels in sync (in case other code relies)
                _stUtc.Text = utcText; _stSunrise.Text = sunriseText; _stSunset.Text = sunsetText;
            }
            catch { }
        }

        private void UpdateInfoColumnWidth()
        {
            try
            {
                if (_listView == null || _listView.IsDisposed) return; if (_listView.Columns.Count == 0) return;
                int fixedWidth = 0; for (int i = 0; i < _listView.Columns.Count - 1; i++) fixedWidth += _listView.Columns[i].Width;

                // Compute actual vertical scrollbar width based on the ListView display area
                int clientW = _listView.ClientSize.Width;
                int vScroll = 0;
                try
                {
                    int displayW = _listView.DisplayRectangle.Width; // excludes visible scrollbars
                    if (displayW > 0 && displayW <= clientW) vScroll = clientW - displayW;
                }
                catch { vScroll = 0; }

                const int fudge = 1;
                int avail = clientW - fixedWidth - vScroll - fudge;
                int info = Math.Max(80, avail);
                _listView.Columns[_listView.Columns.Count - 1].Width = info;
            }
            catch { }
        }

        private void ApplyListFontFromSettings()
        {
            try
            {
                var chosen = _settings.GetDxListFontOrDefault(SystemFonts.MessageBoxFont);
                if (_dxListFont == null ||
                    _dxListFont.FontFamily.Name != chosen.FontFamily.Name ||
                    Math.Abs(_dxListFont.SizeInPoints - chosen.SizeInPoints) > 0.01 ||
                    _dxListFont.Style != chosen.Style)
                {
                    _dxListFont?.Dispose();
                    _dxListFont = chosen; // keep cached settings font

                    // Assign a non-shared clone to the ListView
                    _listView.Font = new Font(_dxListFont, _dxListFont.Style);
                }
            }
            catch { }
            QueueRepaintSpots();
        }

        private void InitializeDxListFont()
        {
            try
            {
                // Build the configured font and keep a cached copy
                _dxListFont?.Dispose();
                _dxListFont = _settings.GetDxListFontOrDefault(SystemFonts.MessageBoxFont);

                // Assign a non-shared clone to the ListView to avoid shared-dispose issues
                _listView.Font = new Font(_dxListFont, _dxListFont.Style);

                // Guard: if something changes the ListView font, re-assert ours
                _listView.FontChanged += (s, e) => ReassertListFont();
            }
            catch { }
            QueueRepaintSpots();
        }

        private void ReassertListFont()
        {
            try
            {
                if (_dxListFont == null)
                    _dxListFont = _settings.GetDxListFontOrDefault(SystemFonts.MessageBoxFont);

                var cur = _listView.Font;
                var refF = _dxListFont;

                // If different by value, set a fresh clone
                if (cur.FontFamily.Name != refF.FontFamily.Name ||
                    Math.Abs(cur.SizeInPoints - refF.SizeInPoints) > 0.01 ||
                    cur.Style != refF.Style)
                {
                    _listView.Font = new Font(refF, refF.Style);
                    QueueRepaintSpots();
                }
            }
            catch
            {
                // If our cached font was invalid/disposed, rebuild and re-apply
                try
                {
                    _dxListFont = _settings.GetDxListFontOrDefault(SystemFonts.MessageBoxFont);
                    _listView.Font = new Font(_dxListFont, _dxListFont.Style);
                    QueueRepaintSpots();
                }
                catch { }
            }
        }

        // === Added back missing helper methods removed during refactor ===
        private void SaveUiSettings()
        {
            try
            {
                _settings.Ui ??= new UiSettings();
                if (WindowState == FormWindowState.Normal)
                {
                    _settings.Ui.Left = Left; _settings.Ui.Top = Top; _settings.Ui.Width = Width; _settings.Ui.Height = Height;
                }
                _settings.Ui.WindowState = (int)WindowState;
                if (_split != null) _settings.Ui.SplitterDistance = _split.SplitterDistance;
                _settings.Ui.EnabledBands = _enabledBands.ToArray();
                _settings.Ui.EnabledModes = _enabledModes.ToArray();
                AppSettings.Save(_settings);
            }
            catch { }
        }

        private void RestoreBandFiltersFromSettings()
        {
            try
            {
                var bands = _settings.Ui?.EnabledBands;
                if (bands != null && bands.Length > 0)
                {
                    _enabledBands.Clear();
                    foreach (var b in bands) _enabledBands.Add(b);
                }
                // Reflect into buttons if already created
                foreach (var btn in _bandButtons)
                {
                    var txt = btn.Text ?? string.Empty;
                    btn.Checked = _enabledBands.Contains(txt);
                }
            }
            catch { }
        }

        private void RestoreModeFiltersFromSettings()
        {
            try
            {
                var modes = _settings.Ui?.EnabledModes;
                var defaults = new[] { "CW", "SSB", "DAT" };
                var src = (modes != null && modes.Length > 0) ? modes : defaults;
                _enabledModes.Clear();
                foreach (var m in src) _enabledModes.Add(m);
                UpdateModeButtonsFromEnabled();
            }
            catch { }
        }

        private void UpdateBandButtonsFromEnabled()
        {
            try
            {
                foreach (var btn in _bandButtons)
                {
                    var b = btn.Text ?? string.Empty;
                    var should = _enabledBands.Contains(b);
                    if (btn.Checked != should) btn.Checked = should;
                }
            }
            catch { }
        }

        private void UpdateModeButtonsFromEnabled()
        {
            try
            {
                foreach (var btn in _modeButtons)
                {
                    var m = btn.Text ?? string.Empty;
                    var should = _enabledModes.Contains(m);
                    if (btn.Checked != should) btn.Checked = should;
                }
            }
            catch { }
        }

        private void MoveCaretToEnd()
        {
            try { _txtConsoleInput.SelectionStart = _txtConsoleInput.TextLength; _txtConsoleInput.SelectionLength = 0; EnsureCursorAfterPrompt(); } catch { }
        }

        private void EnsureCursorAfterPrompt()
        {
            try
            {
                var promptLen = InputPrompt.Length;
                if (_txtConsoleInput.SelectionStart < promptLen) _txtConsoleInput.SelectionStart = promptLen;
                if (_txtConsoleInput.SelectionStart + _txtConsoleInput.SelectionLength < promptLen) _txtConsoleInput.SelectionLength = 0;
            }
            catch { }
        }

        private void TxtConsoleInput_TextChanged(object? sender, EventArgs e)
        {
            if (_txtConsoleInput.TextLength < InputPrompt.Length)
            {
                _txtConsoleInput.Text = InputPrompt;
                MoveCaretToEnd();
            }
        }

        private void TxtConsoleInput_KeyPress(object? sender, KeyPressEventArgs e)
        {
            var promptLen = InputPrompt.Length;
            if (_txtConsoleInput.SelectionStart < promptLen)
            {
                e.Handled = true;
                MoveCaretToEnd();
            }
        }

        private void TxtConsoleInput_KeyDown(object? sender, KeyEventArgs e)
        {
            var promptLen = InputPrompt.Length;
            if (e.KeyCode == Keys.Home)
            {
                _txtConsoleInput.SelectionStart = promptLen; _txtConsoleInput.SelectionLength = 0; e.Handled = true; return;
            }
            if (e.KeyCode == Keys.Back && _txtConsoleInput.SelectionStart <= promptLen)
            {
                e.Handled = true; return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; e.SuppressKeyPress = true; SendConsoleLine();
            }
        }

        private async void SendConsoleLine()
        {
            try
            {
                var text = _txtConsoleInput.Text;
                if (text.Length <= InputPrompt.Length) return;
                var payload = text.Substring(InputPrompt.Length).TrimEnd();
                AppendConsole($"> {payload}\r\n");
                await SendRawThroughManager(payload).ConfigureAwait(true);
            }
            catch { }
            finally
            {
                _txtConsoleInput.Text = InputPrompt; MoveCaretToEnd();
            }
        }

        private void Context_JumpRadio()
        {
            try
            {
                if (_listView.SelectedItems.Count == 0) return;
                var it = _listView.SelectedItems[0];
                var freqText = it.SubItems[1].Text ?? string.Empty;
                int hz = ParseFrequencyToHz(freqText);
                if (hz <= 0) return;
                var mode = InferModeForFrequency(hz);
                var ok = _radio.SendFrequency(hz, mode);
                string band = Utils.FrequencyToBand(hz);
                ShowToastForBand($"Radio: {FormatFreqHz(hz)} {mode} {(ok ? "OK" : "FAILED")}", ok, band);
            }
            catch { }
        }

        private void Context_QrzLookup(bool spotter)
        {
            try
            {
                if (_listView.SelectedItems.Count == 0) return;
                var it = _listView.SelectedItems[0];
                var idx = spotter ? 6 : 0; // 6 = Spotter, 0 = DX Call
                var call = (it.SubItems.Count > idx ? it.SubItems[idx].Text : string.Empty) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(call)) return;

                // Escape each path segment separately so slashes remain as separators
                var escapedPath = string.Join("/",
                    call.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(seg => Uri.EscapeDataString(seg)));

                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://www.qrz.com/db/{escapedPath}",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void Context_DxNews()
        {
            try
            {
                if (_listView.IsDisposed || _listView.SelectedItems.Count == 0) return;
                var it = _listView.SelectedItems[0];
                var call = it.SubItems[0].Text;
                if (string.IsNullOrWhiteSpace(call)) return;

                var escapedPath = string.Join("/",
                    call.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(seg => Uri.EscapeDataString(seg)));

                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://dxnews.com/{escapedPath}/",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void Context_RbnLookupDx()
        {
            try
            {
                if (_listView.SelectedItems.Count == 0) return;
                var it = _listView.SelectedItems[0];
                var call = it.SubItems[0].Text;
                if (string.IsNullOrWhiteSpace(call)) return;
                int hz = ParseFrequencyToHz(it.SubItems.Count > 1 ? (it.SubItems[1].Text ?? string.Empty) : string.Empty);
                if (hz > 0 && IsInSsbSubband(hz)) return; // skip voice subband

                // Query parameter: full value-encoding is correct here
                var url = $"https://www.reversebeacon.net/dxsd1/dxsd1.php?f=0&c={Uri.EscapeDataString(call)}&t=dx";
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try { _cts.Cancel(); } catch { }
            try { _localServer?.Stop(); } catch { }
            try { _localServer?.Dispose(); } catch { }
            try { _clusterManager.Disconnect(); } catch { }
            try { SaveUiSettings(); } catch { }
            try { _clusterManager.LineReceived -= Cluster_LineReceived; } catch { }
            try { _dxListFont?.Dispose(); } catch { }
            // Clean up file logging (Trace only)
            try
            {
                if (_fileTraceListener != null)
                {
                    try { Trace.Listeners.Remove(_fileTraceListener); } catch { }
                    try { _fileTraceListener.Flush(); _fileTraceListener.Close(); } catch { }
                }
            }
            catch { }
            try { _fileLogWriter?.Dispose(); } catch { }
        }

        // === Missing implementations restored ===
        private void PopulateClustersCombo()
        {
            try
            {
                // Hidden combobox retained for compatibility; keep it in sync
                _cmbClusters.Items.Clear();
                foreach (var n in _settings.Clusters.Select(c => c.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                    _cmbClusters.Items.Add(n);
                if (_cmbClusters.Items.Count > 0)
                {
                    var prefer = _settings.LastConnectedCluster;
                    if (!string.IsNullOrWhiteSpace(prefer))
                    {
                        int idx = _cmbClusters.FindStringExact(prefer);
                        _cmbClusters.SelectedIndex = idx >= 0 ? idx : 0;
                    }
                    else _cmbClusters.SelectedIndex = 0;
                }
            }
            catch { }
        }

        private async Task ConsolePumpAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var any = false;
                    while (_rxLines.TryDequeue(out var l))
                    {
                        any = true;
                        AppendConsole(l + "\r\n");
                        AddListViewRawLine(l);
                    }
                    if (!any) await Task.Delay(100, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(250, token).ConfigureAwait(false); }
            }
        }

        private void ScrollSpotsToBottom()
        {
            try
            {
                if (_listView.Items.Count == 0) return;
                int lastReal = FindLastRealItemIndex();
                if (lastReal >= 0) _listView.EnsureVisible(lastReal);
            }
            catch { }
        }

        private int FindLastRealItemIndex()
        {
            for (int i = _listView.Items.Count - 1; i >= 0; i--)
                if (!Equals(_listView.Items[i].Tag, "__filler__"))
                    return i;
            return _listView.Items.Count - 1;
        }

        private Task SendRawThroughManager(string cmd)  
        {
            try { return _clusterManager.SendRawAsync(cmd ?? string.Empty); }
            catch { return Task.CompletedTask; }
        }

        private async void MainForm_Shown(object? sender, EventArgs e)
        {
            try
            {
                EnsureCtyDatLoadedOnBoot();
                // Start local server on app launch if enabled
                try { _localServer?.Stop(); _localServer?.Dispose(); } catch { }
                if (_settings.LocalServerEnabled)
                {
                    _localServer = new LocalClusterServer(_settings.LocalServerPort > 0 ? _settings.LocalServerPort : 7373);
                    _localServer.CommandReceived += async (cmd) => await SendRawThroughManager(cmd);
                    _localServer.ClientCountChanged += count => UpdateLocalServerIndicator(count);
                    _localServer.Start();
                    UpdateLocalServerIndicator(_localServer.ClientCount);
                }
                else
                {
                    _localServer = null;
                    _stLocalServer.Text = "Server: disabled";
                    _stLocalServer.ForeColor = SystemColors.ControlText;
                }

                var target = SelectedClusterActualName();
                if (string.IsNullOrWhiteSpace(target)) target = _settings.LastConnectedCluster;
                if (!string.IsNullOrWhiteSpace(target)) await AutoConnectAsync(target!);
            }
            catch { }
        }

        private void EnsureCtyDatLoadedOnBoot()
        {
            try
            {
                _cty = CtyDat.Load();
                if (_cty.IsLoaded) return;

                if (_settings.CheckCtyOnBoot)
                {
                    var res = MessageBox.Show(this, "CTY.DAT was not found. Do you want to download it now?", "CTY.DAT Missing", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (res == DialogResult.Yes)
                    {
                        _cty = CtyDat.Load();
                    }
                }
            }
            catch { }
        }

        private async Task AutoConnectAsync(string target)
        {
            if (!ValidateProfileBeforeConnect()) { _lblStatus.Text = "Status: Profile incomplete"; return; }
            if (string.Equals(_clusterManager.ActiveClusterName, target, StringComparison.OrdinalIgnoreCase)) { UpdateClusterIndicator(); return; }
            if (!(_cty?.IsLoaded ?? false)) { _cty = CtyDat.Load(); }
            if (!(_cty?.IsLoaded ?? false)) { _lblStatus.Text = "Status: CTY.DAT not loaded"; UpdateClusterIndicator(); return; }

            _lblStatus.Text = $"Status: Auto-connecting to {target}...";
            using var cts = new CancellationTokenSource(10000);
            var ok = await _clusterManager.ConnectAsync(target, cts.Token, forceLogin: true).ConfigureAwait(true);
            if (ok)
            {
                _btnConnect.Text = "Disconnect";
                _lblStatus.Text = $"Connected to {target} as {(_settings.MyCall ?? string.Empty).ToUpperInvariant()}";
                _lastConnectedCluster = target;
                _settings.LastConnectedCluster = target;
                UpdateClusterIndicator();
                RebindShortcutMenuForCurrentCluster(); // ensure menu reflects this cluster
                AppendConsole($"[SYS] Auto-connected to {target}\r\n");
                try { AppSettings.Save(_settings); } catch { }
            }
            else
            {
                _lblStatus.Text = "Status: Disconnected (auto-connect failed)";
                UpdateClusterIndicator();
            }
        }

        private async void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (!ValidateProfileBeforeConnect()) return;
            var target = SelectedClusterActualName();
            if (string.IsNullOrWhiteSpace(target)) { MessageBox.Show(this, "Select a cluster first."); return; }

            if (string.Equals(_clusterManager.ActiveClusterName, target, StringComparison.OrdinalIgnoreCase))
            {
                _clusterManager.Disconnect(target);
                _btnConnect.Text = "Connect";
                _lblStatus.Text = "Status: Disconnected";
                UpdateClusterIndicator();
                RebindShortcutMenuForCurrentCluster(); // update menu (no active cluster)
                return;
            }

            _cty = CtyDat.Load();
            if (!(_cty?.IsLoaded ?? false))
            {
                _lblStatus.Text = "Status: CTY.DAT not loaded";
                MessageBox.Show(this, "CTY.DAT is required before connecting. Place 'cty.dat' next to the EXE, then try again.", "CTY.DAT missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateProfileBeforeConnect()) return;

            _lblStatus.Text = "Status: Connecting...";
            using var cts = new CancellationTokenSource(10000);
            var ok2 = await _clusterManager.ConnectAsync(target, cts.Token, forceLogin: true).ConfigureAwait(true);
            if (ok2)
            {
                _btnConnect.Text = "Disconnect";
                _lblStatus.Text = $"Connected to {target} as {(_settings.MyCall ?? string.Empty).ToUpperInvariant()}";
                _lastConnectedCluster = target;
                _settings.LastConnectedCluster = target;
                UpdateClusterIndicator();
                RebindShortcutMenuForCurrentCluster(); // ensure menu reflects this cluster
                AppendConsole($"[SYS] Connected to {target}\r\n");
                try { AppSettings.Save(_settings); } catch { }
            }
            else
            {
                _lblStatus.Text = "Status: Connect Failed";
                MessageBox.Show(this, "Connect failed.");
            }
        }

        private void BtnSettings_Click(object? sender, EventArgs e)
        {
            using var dlg = new SettingsForm(_settings);
            dlg.StartPosition = FormStartPosition.Manual; // allow custom positioning
            dlg.ShowInTaskbar = false;
            dlg.TopMost = this.TopMost;
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // Apply local server changes
                try { _localServer?.Stop(); _localServer?.Dispose(); } catch { }
                if (_settings.LocalServerEnabled)
                {
                    _localServer = new LocalClusterServer(_settings.LocalServerPort > 0 ? _settings.LocalServerPort : 7373);
                    _localServer.CommandReceived += async (cmd) => await SendRawThroughManager(cmd);
                    _localServer.ClientCountChanged += count => UpdateLocalServerIndicator(count);
                    _localServer.Start();
                    UpdateLocalServerIndicator(_localServer.ClientCount);
                }
                else
                {
                    _localServer = null;
                    _stLocalServer.Text = "Server: disabled";
                    _stLocalServer.ForeColor = SystemColors.ControlText;
                }

                // Apply CAT changes dynamically to the existing radio controller
                try
                {
                    _radio.Enabled = _settings.CatEnabled;
                    _radio.Port = _settings.CatPort;
                    _radio.Baud = _settings.CatBaud;
                    _radio.Rig = _settings.Rig;
                    _radio.IcomAddress = _settings.IcomAddress;
                }
                catch { }

                // Apply any DX list font change
                ApplyListFontFromSettings();

                PopulateClustersCombo();
                UpdateClusterIndicator();
            }
        }

        // Opens the ClusterEditorForm for the current/last/first cluster and saves changes
        private void MiClusters_Click(object? sender, EventArgs e)
        {
            try
            {
                // Pick an initial cluster to edit
                var name = _clusterManager.ActiveClusterName
                           ?? _settings.LastConnectedCluster
                           ?? (_cmbClusters.Items.Count > 0 ? _cmbClusters.Items[0]?.ToString() : null);

                ClusterDefinition? existing = null;
                if (!string.IsNullOrWhiteSpace(name))
                    existing = _settings.Clusters.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

                // Pass a copy into the editor to avoid mutating the live instance during Cancel
                using var editor = new ClusterEditorForm(existing != null ? existing with { } : null);
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    var updated = editor.Def;

                    // Insert or replace
                    int idx = -1;
                    if (existing != null)
                        idx = _settings.Clusters.FindIndex(c => c.Name.Equals(existing.Name, StringComparison.OrdinalIgnoreCase));

                    if (idx >= 0) _settings.Clusters[idx] = updated; else _settings.Clusters.Add(updated);

                    // Enforce single AutoLogin
                    if (updated.AutoLogin)
                    {
                        for (int i = 0; i < _settings.Clusters.Count; i++)
                        {
                            if (!ReferenceEquals(_settings.Clusters[i], updated))
                            {
                                var ci = _settings.Clusters[i];
                                _settings.Clusters[i] = ci with { AutoLogin = false };
                            }
                        }
                    }

                    // Persist and refresh
                    try { AppSettings.Save(_settings); } catch { }
                    PopulateClustersCombo();
                    UpdateClusterIndicator();
                    RebindShortcutMenuForCurrentCluster(); // updated shortcuts/names
                }
            }
            catch (Exception ex)
            {
                try { MessageBox.Show(this, $"Cluster edit failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            }
        }

        private void UpdateClusterIndicator()
        {
            try
            {
                var active = _clusterManager.ActiveClusterName;
                var shown = !string.IsNullOrWhiteSpace(active) ? active : (!string.IsNullOrWhiteSpace(_lastConnectedCluster) ? _lastConnectedCluster : _cmbClusters.SelectedItem as string ?? string.Empty);
                var call = (_settings.MyCall ?? string.Empty).ToUpperInvariant();
                _stCluster.Text = string.IsNullOrWhiteSpace(shown)
                    ? "Cluster: (none)"
                    : (string.IsNullOrWhiteSpace(call) ? $"Cluster: {shown}" : $"Cluster: {shown} (as {call})");
                Text = string.IsNullOrWhiteSpace(shown) ? _baseTitle : ($"{_baseTitle} - {shown}");
            }
            catch { }
        }

        private void ShowToastForBand(string message, bool success, string? band)
        {
            if (InvokeRequired) { try { BeginInvoke((Action)(() => ShowToastForBand(message, success, band))); } catch { } return; }
            try
            {
                var back = string.IsNullOrWhiteSpace(band) ? SystemColors.Info : _settings.ColorForBand(band);
                var brightness = back.GetBrightness();
                var fore = (brightness < 0.5f) ? Color.White : Color.Black;
                var border = success ? Color.ForestGreen : Color.IndianRed;
                var toast = new ToastForm(message, back, fore, border, 5000, this) { TopMost = this.TopMost };
                toast.Show(this);
            }
            catch { }
        }

        // FIX: restore original signature (string line) instead of int
        private void Cluster_LineReceived(string clusterName, string line)
        {
            _rxLines.Enqueue($"[{clusterName}] {line}");
            try { if (_localServer != null && _localServer.ClientCount > 0) _localServer.BroadcastLine(line); } catch { }
        }

        private static (DateTime? Rise, DateTime? Set) ComputeSunriseSunsetUtc(DateTime dateUtc, string grid)
        {
            if (!Utils.TryGridToLatLon(grid ?? string.Empty, out var lat, out var lon)) return (null, null);
            try
            {
                var sr = CalcSunUtc(dateUtc, lat, lon, true);
                var ss = CalcSunUtc(dateUtc, lat, lon, false);
                return (sr, ss);
            }
            catch { return (null, null); }
        }

        private static DateTime? CalcSunUtc(DateTime dateUtc, double latDeg, double lonDeg, bool sunrise)
        {
            double zenith = 90.833;
            double D2R(double d) => Math.PI / 180.0 * d;
            double R2D(double r) => 180.0 / Math.PI * r;

            int N = dateUtc.DayOfYear;
            double lngHour = lonDeg / 15.0;
            double t = N + ((sunrise ? 6.0 : 18.0) - lngHour) / 24.0;
            double M = (0.9856 * t) - 3.289;
            double L = M + (1.916 * Math.Sin(D2R(M))) + (0.020 * Math.Sin(D2R(2 * M))) + 282.634;
            L = (L % 360 + 360) % 360;

            double RA = R2D(Math.Atan(0.91764 * Math.Tan(D2R(L))));
            RA = (RA % 360 + 360) % 360;
            double Lquadrant = Math.Floor(L / 90.0) * 90.0;
            double RAquadrant = Math.Floor(RA / 90.0) * 90.0;
            RA = RA + (Lquadrant - RAquadrant);
            RA /= 15.0;

            double sinDec = 0.39782 * Math.Sin(D2R(L));
            double cosDec = Math.Cos(Math.Asin(sinDec));

            double cosH = (Math.Cos(D2R(zenith)) - (sinDec * Math.Sin(D2R(latDeg)))) / (cosDec * Math.Cos(D2R(latDeg)));
            if (cosH > 1) return null;
            if (cosH < -1) return null;

            double H = sunrise ? 360.0 - R2D(Math.Acos(cosH)) : R2D(Math.Acos(cosH));
            H /= 15.0;

            double T = H + RA - (0.06571 * t) - 6.622;
            double UT = T - lngHour;
            UT = (UT % 24 + 24) % 24;

            int hours = (int)Math.Floor(UT);
            int minutes = (int)Math.Floor((UT - hours) * 60.0);
            int seconds = (int)Math.Round((((UT - hours) * 60.0) - minutes) * 60.0);
            if (seconds == 60) { seconds = 0; minutes++; }
            if (minutes == 60) { minutes = 0; hours = (hours + 1) % 24; }

            return new DateTime(dateUtc.Year, dateUtc.Month, dateUtc.Day, hours, minutes, seconds, DateTimeKind.Utc);
        }

        // Mode inference helpers
        private string InferModeForFrequency(int hz)
        {
            // hz already in Hz. Provide detailed inference path.
            string mode;
            string reason;
            if (IsInDigitalSubband(hz))
            {
                mode = "DATA";
                reason = "frequency in known digital subband";
            }
            else if (IsInSsbSubband(hz))
            {
                var band = Utils.FrequencyToBand(hz);
                bool lsb = string.Equals(band, "160m", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(band, "80m", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(band, "40m", StringComparison.OrdinalIgnoreCase);
                mode = lsb ? "LSB" : "USB";
                reason = $"frequency in SSB subband for {band}, sideband={(lsb ? "LSB" : "USB")}"; 
            }
            else
            {
                mode = "CW";
                reason = "default fallback (not digital/SSB range)";
            }

            if (_settings.DebugLogEnabled)
            {
                try
                {
                    Debug.WriteLine($"[ModeInfer:Freq] hz={hz} ({FormatFreqHz(hz)}), mode={mode}, reason={reason}");
                }
                catch { }
            }
            return mode;
        }

        private string InferModeForSpot(ListViewItem it)
        {
            try
            {
                int hz = 0;
                if (it.SubItems.Count > 1)
                    hz = ParseFrequencyToHz((it.SubItems[1].Text ?? string.Empty).Replace(',', '.'));

                var info = (it.SubItems.Count > 7 ? it.SubItems[7].Text : string.Empty) ?? string.Empty;

                string mode;
                string reason;

                bool looksDigital = LooksDigital(info);
                bool freqDigital = hz > 0 && IsInDigitalSubband(hz);
                bool freqSsb = hz > 0 && IsInSsbSubband(hz);

                if (looksDigital || freqDigital)
                {
                    mode = "DAT";
                    reason = looksDigital
                        ? $"info text matched digital pattern: '{info}'"
                        : "frequency in digital subband";
                }
                else if (freqSsb)
                {
                    mode = "SSB";
                    reason = "frequency in SSB subband";
                }
                else
                {
                    mode = "CW";
                    reason = "default fallback";
                }

                if (_settings.DebugLogEnabled)
                {
                    try
                    {
                        Debug.WriteLine($"[ModeInfer:Spot] hz={(hz > 0 ? hz.ToString() : "n/a")}, info='{info}', mode={mode}, reason={reason}");
                    }
                    catch { }
                }
                return mode;
            }
            catch
            {
                if (_settings.DebugLogEnabled)
                {
                    try { Debug.WriteLine("[ModeInfer:Spot] exception -> default CW"); } catch { }
                }
                return "CW";
            }
        }

        private bool ValidateProfileBeforeConnect()
        {
            try
            {
                var call = (_settings.MyCall ?? string.Empty).Trim().ToUpperInvariant();
                var grid = (_settings.GridSquare ?? string.Empty).Trim().ToUpperInvariant();
                bool callOk = !string.IsNullOrWhiteSpace(call) && !string.Equals(call, "MYCALL", StringComparison.OrdinalIgnoreCase);
                bool gridOk = !string.IsNullOrWhiteSpace(grid) && grid.Length >= 4;
                if (!callOk || !gridOk)
                {
                    var msg = "You must enter a valid callsign and at least a 4-character Grid Square in Settings before connecting.";
                    MessageBox.Show(this, msg, "Profile Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                _settings.MyCall = call; _settings.GridSquare = grid; AppSettings.Save(_settings);
                return true;
            }
            catch { return false; }
        }

        private string? SelectedClusterActualName()
        {
            return _clusterManager.ActiveClusterName ?? _settings.LastConnectedCluster ?? (_cmbClusters.SelectedItem as string);
        }

        private static bool LooksDigital(string info)
        {
            if (string.IsNullOrWhiteSpace(info)) return false;
            return Regex.IsMatch(info, @"\b(FT8|FT4|MSHV|MSK144|JT65|JT9|JS8|F/H|F\s*H|PKT|PSK31|PSK|RTTY)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsInDigitalSubband(int hz)
        {
            var ft8 = new (int center, int halfWin)[]
            {
                (1840000, 4000), (3573000, 4000), (5357000, 4000), (7074000, 4000),
                (10136000, 4000), (14074000, 4000), (18100000, 4000), (21074000, 4000),
                (24915000, 4000), (28074000, 4000), (50313000, 4000),
            };
            var ft4 = new (int center, int halfWin)[]
            {
                (3575000, 4000), (7047500, 4000), (10140000, 4000), (14080000, 4000),
                (18104000, 4000), (21140000, 4000), (24918000, 4000), (28180000, 4000),
                (50318000, 4000),
            };
            static bool InRanges((int center, int halfWin)[] ranges, int f)
            {
                foreach (var r in ranges) if (f >= r.center - r.halfWin && f <= r.center + r.halfWin) return true; return false;
            }
            return InRanges(ft8, hz) || InRanges(ft4, hz);
        }

        private static bool IsInSsbSubband(int hz)
        {
            var ranges = new (int lo, int hi)[]
            {
                (1840000, 2000000), // 160m
                (3600000, 4000000), // 80m
                (7125000, 7300000), // 40m
                (14150000, 14350000), // 20m
                (18110000, 18168000), // 17m
                (21200000, 21450000), // 15m
                (24930000, 24990000), // 12m
                (28300000, 29700000), // 10m
                (50100000, 50400000), // 6m
            };
            foreach (var r in ranges) if (hz >= r.lo && hz <= r.hi) return true; return false;
        }

        private ContextMenuStrip BuildShortcutMenu()
        {
            return BuildShortcutMenu(_txtConsoleInput);
        }

        private ContextMenuStrip BuildShortcutMenu(TextBox target)
        {
            var menu = new ContextMenuStrip();

            // Use cluster-specific shortcuts only
            string? clusterName = _clusterManager.ActiveClusterName ?? _settings.LastConnectedCluster;
            var def = (!string.IsNullOrWhiteSpace(clusterName))
                ? _settings.Clusters.FirstOrDefault(c => string.Equals(c.Name, clusterName, StringComparison.OrdinalIgnoreCase))
                : null;

            bool any = false;
            try
            {
                var list = def?.CommandShortcuts;
                if (list != null && list.Count > 0)
                {
                    foreach (var sc in list)
                    {
                        if (string.IsNullOrWhiteSpace(sc.Name) || string.IsNullOrWhiteSpace(sc.Command)) continue;
                        any = true;
                        var mi = new ToolStripMenuItem(sc.Name) { Tag = sc, ToolTipText = $"Cluster: {def!.Name}" };
                        mi.Click += async (sender, args) =>
                        {
                            if (mi.Tag is CommandShortcut cmd)
                                await SendShortcutAsync(cmd);
                        };
                        menu.Items.Add(mi);
                    }
                }
            }
            catch { }

            if (!any)
            {
                var label = string.IsNullOrWhiteSpace(clusterName) ? "(No cluster selected)" : $"(No shortcuts for '{clusterName}')";
                var miNone = new ToolStripMenuItem(label) { Enabled = false };
                menu.Items.Add(miNone);
            }

            menu.Items.Add(new ToolStripSeparator());

            var miRefresh = new ToolStripMenuItem("Refresh Shortcuts", null, (s, e) => RefreshShortcutMenu());
            var miEditJson = new ToolStripMenuItem("Edit Shortcuts (JSON)", null, (s, e) =>
            {
                try
                {
                    var path = System.IO.Path.Combine(AppContext.BaseDirectory, "ZVClusterApp.settings.json");
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                catch { }
            })
            {
                ToolTipText = "Open settings file to edit cluster-specific shortcuts"
            };

            menu.Items.Add(miRefresh);
            menu.Items.Add(miEditJson);

            return menu;
        }

        private void RefreshShortcutMenu()
        {
            try
            {
                var reloaded = AppSettings.Load();
                // Replace clusters with latest definitions (no global shortcuts retained)
                _settings.Clusters = reloaded.Clusters;
                _shortcutMenu = BuildShortcutMenu(_txtConsoleInput);
                _txtConsoleInput.ContextMenuStrip = _shortcutMenu;
            }
            catch { }
        }

        private async Task SendShortcutAsync(CommandShortcut sc)
        {
            try
            {
                var expanded = ExpandShortcutText(sc.Command);
                var lines = expanded
                    .Replace("\r\n", "\n")
                    .Replace("\r", "\n")
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToArray();

                foreach (var line in lines)
                {
                    AppendConsole($"> {line}\r\n");
                    await SendRawThroughManager(line).ConfigureAwait(true);
                }
            }
            catch { }
        }

        private string ExpandShortcutText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            try
            {
                var nowUtc = DateTime.UtcNow;
                var local = nowUtc + TimeSpan.FromHours(_settings.GmtOffsetHours);
                string repl(string token) => token switch
                {
                    "MYCALL" => (_settings.MyCall ?? string.Empty).ToUpperInvariant(),
                    "CALL" => (_settings.MyCall ?? string.Empty).ToUpperInvariant(),
                    "GRID" => (_settings.GridSquare ?? string.Empty).Trim().ToUpperInvariant(),
                    "UTC" => nowUtc.ToString("HHmm", CultureInfo.InvariantCulture),
                    "LOCAL" => local.ToString("HHmm", CultureInfo.InvariantCulture),
                    "DATEUTC" => nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    "DATELocal" => local.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    _ => "{" + token + "}"
                };

                var result = Regex.Replace(raw, @"\{([A-Za-z0-9_]+)\}", m => repl(m.Groups[1].Value));
                return result;
            }
            catch { return raw; }
        }

        // Rebuild the console input context menu for the currently active/last cluster
        private void RebindShortcutMenuForCurrentCluster()
        {
            try
            {
                _shortcutMenu = BuildShortcutMenu(_txtConsoleInput);
                _txtConsoleInput.ContextMenuStrip = _shortcutMenu;
            }
            catch { }
        }

        private void MiExit_Click(object? sender, EventArgs e)
        {
            try { Close(); } catch { Application.Exit(); }
        }
    }

    internal sealed class BandColorRenderer : ToolStripProfessionalRenderer
    {
        private readonly AppSettings _settings;
        private readonly HashSet<string> _bands;
        private readonly HashSet<string> _modes;

        public BandColorRenderer(AppSettings settings, HashSet<string> bands, HashSet<string> modes)
        {
            _settings = settings; _bands = bands; _modes = modes;
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is ToolStripButton btn)
            {
                var g = e.Graphics;
                var rect = new Rectangle(Point.Empty, e.Item.Size);

                if (_bands.Contains(btn.Text ?? string.Empty))
                {
                    var baseColor = _settings.ColorForBand(btn.Text ?? string.Empty);
                    var uncheckedBack = Color.FromArgb(60, 60, 60); // dark grey
                    var back = btn.Checked ? baseColor : uncheckedBack;
                    var borderColor = btn.Checked ? ControlPaint.Dark(baseColor) : Color.DimGray;

                    using (var bFill = new SolidBrush(back))
                        g.FillRectangle(bFill, rect);

                    using (var penBorder = new Pen(borderColor))
                        g.DrawRectangle(penBorder, new Rectangle(0, 0, rect.Width - 1, rect.Height - 1));

                    if (!btn.Checked)
                    {
                        var xColor = btn.Enabled ? Color.Gainsboro : ControlPaint.Dark(Color.Gainsboro);
                        var prev = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        const int pad = 4;
                        using (var penX = new Pen(xColor, 2f))
                        {
                            g.DrawLine(penX, pad, pad, rect.Width - pad - 1, rect.Height - pad - 1);
                            g.DrawLine(penX, rect.Width - pad - 1, pad, pad, rect.Height - pad - 1);
                        }
                        g.SmoothingMode = prev;
                    }
                    return;
                }

                if (_modes.Contains(btn.Text ?? string.Empty))
                {
                    var onColor = Color.FromArgb(96, Color.LightGreen);
                    var uncheckedBack = Color.FromArgb(60, 60, 60);
                    var back = btn.Checked ? onColor : uncheckedBack;
                    var borderColor = btn.Checked ? Color.SeaGreen : Color.DimGray;

                    using (var bFill = new SolidBrush(back))
                        g.FillRectangle(bFill, rect);

                    using (var penBorder = new Pen(borderColor))
                        g.DrawRectangle(penBorder, new Rectangle(0, 0, rect.Width - 1, rect.Height - 1));

                    if (!btn.Checked)
                    {
                        var xColor = btn.Enabled ? Color.Gainsboro : ControlPaint.Dark(Color.Gainsboro);
                        var prev = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        const int pad = 4;
                        using (var penX = new Pen(xColor, 2f))
                        {
                            g.DrawLine(penX, pad, pad, rect.Width - pad - 1, rect.Height - pad - 1);
                            g.DrawLine(penX, rect.Width - pad - 1, pad, pad, rect.Height - pad - 1);
                        }
                        g.SmoothingMode = prev;
                    }
                    return;
                }
            }
            base.OnRenderButtonBackground(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var text = e.Text ?? string.Empty;

            if (_bands.Contains(text))
            {
                if (e.Item is ToolStripButton b && !b.Checked)
                    e.TextColor = Color.Gainsboro;
                else
                {
                    var c = _settings.ColorForBand(text);
                    e.TextColor = c.GetBrightness() < 0.5f ? Color.White : Color.Black;
                }
            }
            else if (_modes.Contains(text))
            {
                if (e.Item is ToolStripButton b && !b.Checked)
                    e.TextColor = Color.Gainsboro;
                else
                    e.TextColor = Color.Black;
            }

            base.OnRenderItemText(e);
        }
    }

    static class UiUtil
    {
        public static void Suspend(Control c)
        {
            try
            {
                if (c.Controls.Count == 0) return;
                foreach (Control cc in c.Controls) Suspend(cc);
                c.SuspendLayout();
            }
            catch { }
        }
        public static void Resume(Control c)
        {
            try
            {
                c.ResumeLayout(true);
                foreach (Control cc in c.Controls) Resume(cc);
            }
            catch { }
        }
    }
}
