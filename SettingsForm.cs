using System;
using System.ComponentModel;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace ZVClusterApp.WinForms {
    [DesignerCategory("Form")]
    public partial class SettingsForm : Form {
        private readonly AppSettings _settings;
        // Work on a deep copy and only apply changes back on OK
        private AppSettings _working = null!;

        // Expose a colors object for designer/property binding if needed
        [Browsable(false)]
        public BandColorsModel DesignerColors { get; } = new BandColorsModel();

        // Ensure consistent band order
        private static readonly string[] BandOrder = new[] { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m" };

        // Control fields (formerly in Designer)
        private ListView _lvClusters = null!;
        private ListView _lvColors = null!;
        private Button _btnOk = null!;
        private Button _btnCancel = null!;
        private Button _btnAdd = null!;
        private Button _btnEdit = null!;
        private Button _btnRemove = null!;
        private TextBox _txtMyCall = null!;
        private TextBox _txtName = null!;
        private NumericUpDown _numGmt = null!;
        private TextBox _txtQth = null!;
        private TextBox _txtGrid = null!;
        private NumericUpDown _numServerPort = null!;
        private Label lblServerPort = null!;
        private Label lblSources = null!;
        private Label lblGmt = null!;
        private Label lblMyCall = null!;
        private Label lblName = null!;
        private Label lblQth = null!;
        private Label lblGrid = null!;
        private Label lblColors = null!;

        // New: CAT group box controls
        private GroupBox _grpCat = null!;
        private ComboBox _cmbCatPort = null!;
        private ComboBox _cmbCatBaud = null!;
        private ComboBox _cmbRig = null!;
        private Label _lblCatPort = null!;
        private Label _lblCatBaud = null!;
        private Label _lblRig = null!;
        private CheckBox _chkCatEnabled = null!;
        private Label _lblCiv = null!;
        private NumericUpDown _numCiv = null!;

        // New: Spot Server group controls
        private GroupBox _grpSpot = null!;
        private CheckBox _chkUseKm = null!; // toggle for distance units
        private CheckBox _chkCtyBoot = null!; // check CTY.DAT on boot
        private CheckBox _chkServerEnabled = null!; // enable local server
        private CheckBox _chkDebugLog = null!; // enable debug logging (new)

        // New: User Profile group controls
        private GroupBox _grpUser = null!;

        // New: Spot Sources group controls
        private GroupBox _grpSources = null!;

        // New: Colors group controls
        private GroupBox _grpColors = null!;

        // New: CTY.DAT group controls
        private GroupBox _grpCty = null!;

        // New: Appearance group for DX list font
        private GroupBox _grpAppearance = null!;
        private Label _lblDxFont = null!;
        private Label _lblDxFontSample = null!;
        private Button _btnChooseDxFont = null!;
        private Font? _pendingDxListFont;

        public SettingsForm(AppSettings settings) {
            _settings = settings; Text = "Settings";
            // Create a deep working copy of provided settings
            _working = CloneSettings(_settings);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi; // scale control sizes/positions for high DPI
            AutoScroll = true; // allow scrolling if content exceeds client area
            InitializeComponent();
            try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BindSettingsToUi();
        }

        // Parameterless constructor for WinForms Designer
        public SettingsForm() : this(LoadSettingsForDesigner()) {
            Text = "Settings";
        }

        private static AppSettings LoadSettingsForDesigner() {
            try { return AppSettings.Load(); } catch { return CreateDesignTimeSettings(); }
        }

        private static AppSettings CreateDesignTimeSettings() {
            var s = new AppSettings {
                MyCall = "MYCALL",
                Name = "Your Name",
                GmtOffsetHours = 0,
                QTH = "City, Country",
                GridSquare = "AA00aa"
            };
            // Seed two example nodes with a single AutoLogin
            s.Clusters.Add(new ClusterDefinition { Name = "Example 1", Host = "example.com", Port = 7000, Format = ClusterFormat.DXSpider, AutoLogin = true });
            s.Clusters.Add(new ClusterDefinition { Name = "Example 2", Host = "cluster.local", Port = 7300, Format = ClusterFormat.CCCluster, AutoLogin = false });
            s.LocalServerPort = 7373;
            return s;
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            // Restore absolute location only (no relative owner offset)
            try {
                if (_settings.SettingsLeft != 0 || _settings.SettingsTop != 0)
                {
                    StartPosition = FormStartPosition.Manual;
                    Left = _settings.SettingsLeft; Top = _settings.SettingsTop;
                }
            } catch { }
            // Always refresh COM ports when opened
            PopulateCatControls();
            AdjustFormSizeToContent();
            try { _btnCancel?.Select(); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try
            {
                // Persist window location to in-memory settings if normal state (disk save is only on OK)
                if (WindowState == FormWindowState.Normal)
                {
                    // Save absolute position only
                    _settings.SettingsLeft = Left;
                    _settings.SettingsTop = Top;
                    // Do NOT persist to disk here; only the OK path should save.
                    // Previously: AppSettings.Save(_settings);
                }
            }
            catch { }
        }

        // Dispose (no components container needed)
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
        }

        // InitializeComponent moved from Designer
        private void InitializeComponent() {
            _lvClusters = new ListView();
            lblSources = new Label();
            _numGmt = new NumericUpDown();
            lblGmt = new Label();
            _txtMyCall = new TextBox();
            lblMyCall = new Label();
            _btnAdd = new Button();
            _btnEdit = new Button();
            _btnRemove = new Button();
            lblName = new Label();
            _txtName = new TextBox();
            lblQth = new Label();
            _txtQth = new TextBox();
            lblGrid = new Label();
            _txtGrid = new TextBox();
            lblColors = new Label();
            _lvColors = new ListView();
            _btnOk = new Button();
            _btnCancel = new Button();
            _numServerPort = new NumericUpDown();
            lblServerPort = new Label();
            _grpCat = new GroupBox();
            _cmbCatPort = new ComboBox();
            _cmbCatBaud = new ComboBox();
            _cmbRig = new ComboBox();
            _lblCatPort = new Label();
            _lblCatBaud = new Label();
            _lblRig = new Label();
            _chkCatEnabled = new CheckBox();
            _lblCiv = new Label();
            _numCiv = new NumericUpDown();
            _grpSpot = new GroupBox();
            _chkUseKm = new CheckBox();
            _chkCtyBoot = new CheckBox();
            _chkServerEnabled = new CheckBox();
            _chkDebugLog = new CheckBox();
            _grpUser = new GroupBox();
            _grpSources = new GroupBox();
            _grpColors = new GroupBox();
            _grpCty = new GroupBox();
            // Appearance controls
            _grpAppearance = new GroupBox();
            _lblDxFont = new Label();
            _lblDxFontSample = new Label();
            _btnChooseDxFont = new Button();
            ((System.ComponentModel.ISupportInitialize)_numGmt).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_numServerPort).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_numCiv).BeginInit();
            SuspendLayout();
            // Spot Sources group (top)
            _grpSources.Text = "Spot Sources";
            _grpSources.Location = new Point(12, 8);
            _grpSources.Size = new Size(868, 160);
            _grpSources.Name = "_grpSources";

            lblSources.Location = new Point(12, 22);
            lblSources.Size = new Size(640, 18);
            lblSources.Text = "Spot Sources (double click to edit)";

            _lvClusters.FullRowSelect = true;
            _lvClusters.HideSelection = false;
            _lvClusters.Location = new Point(12, 44);
            _lvClusters.Size = new Size(640, 100);
            _lvClusters.View = View.Details;
            _lvClusters.Sorting = SortOrder.Ascending;
            _lvClusters.MouseDoubleClick += LvClusters_MouseDoubleClick;

            _btnAdd.Text = "Add"; _btnAdd.Size = new Size(100, 23); _btnAdd.Location = new Point(670, 44); _btnAdd.Click += BtnAdd_Click;
            _btnEdit.Text = "Edit"; _btnEdit.Size = new Size(100, 23); _btnEdit.Location = new Point(670, 73); _btnEdit.Click += BtnEdit_Click;
            _btnRemove.Text = "Remove"; _btnRemove.Size = new Size(100, 23); _btnRemove.Location = new Point(670, 102); _btnRemove.Click += BtnRemove_Click;

            _grpSources.Controls.AddRange(new Control[] { lblSources, _lvClusters, _btnAdd, _btnEdit, _btnRemove });

            // User Profile group under sources
            _grpUser.Text = "User Profile";
            _grpUser.Location = new Point(12, 180);
            _grpUser.Size = new Size(540, 140);
            _grpUser.Name = "_grpUser";
            lblMyCall.Location = new Point(12, 28); lblMyCall.Size = new Size(54, 23); lblMyCall.Text = "My Call:";
            _txtMyCall.Location = new Point(90, 25); _txtMyCall.Size = new Size(100, 23);
            lblName.Location = new Point(210, 28); lblName.Size = new Size(50, 23); lblName.Text = "Name:";
            _txtName.Location = new Point(265, 25); _txtName.Size = new Size(250, 23);
            lblGmt.Location = new Point(12, 60); lblGmt.Size = new Size(74, 23); lblGmt.Text = "GMT Offset:";
            _numGmt.Location = new Point(90, 58); _numGmt.Maximum = 14; _numGmt.Minimum = -14; _numGmt.Size = new Size(80, 23);
            lblQth.Location = new Point(210, 60); lblQth.Size = new Size(40, 23); lblQth.Text = "QTH:";
            _txtQth.Location = new Point(265, 58); _txtQth.Size = new Size(250, 23);
            lblGrid.Location = new Point(12, 92); lblGrid.Size = new Size(73, 20); lblGrid.Text = "Grid Square:";
            _txtGrid.Location = new Point(90, 90); _txtGrid.Size = new Size(100, 23);
            _grpUser.Controls.AddRange(new Control[] { lblMyCall, _txtMyCall, lblName, _txtName, lblGmt, _numGmt, lblQth, _txtQth, lblGrid, _txtGrid });

            // Spot Server group under user profile
            _grpSpot = new GroupBox { Text = "Spot Server", Location = new Point(12, 330), Size = new Size(540, 110), Name = "_grpSpot" };
            lblServerPort.Text = "Port:"; lblServerPort.AutoSize = true; lblServerPort.Location = new Point(15, 32);
            _numServerPort.Minimum = 1; _numServerPort.Maximum = 65535; _numServerPort.Value = 7373; _numServerPort.Location = new Point(60, 28); _numServerPort.Size = new Size(80, 23);
            _chkUseKm = new CheckBox { Text = "Use kilometers for distances", Location = new Point(160, 30), AutoSize = true };
            _chkCtyBoot = new CheckBox { Text = "Check CTY.DAT on startup", Location = new Point(350, 30), AutoSize = true };
            _chkServerEnabled = new CheckBox { Text = "Enable server", Location = new Point(160, 60), AutoSize = true };
            _chkDebugLog = new CheckBox { Text = "Enable debug logging:", Location = new Point(350, 60), AutoSize = true, Checked = false };
            _grpSpot.Controls.AddRange(new Control[] { lblServerPort, _numServerPort, _chkUseKm, _chkCtyBoot, _chkServerEnabled, _chkDebugLog });

            // Colors group
            _grpColors.Text = "Colors"; _grpColors.Location = new Point(12, 420); _grpColors.Size = new Size(370, 330); _grpColors.Name = "_grpColors";
            lblColors.Location = new Point(12, 22); lblColors.Size = new Size(300, 18); lblColors.Text = "Per-band colors (double-click to change):";
            _lvColors.FullRowSelect = true; _lvColors.Location = new Point(12, 48); _lvColors.Size = new Size(340, 260); _lvColors.UseCompatibleStateImageBehavior = false; _lvColors.View = View.Details; _lvColors.MouseDoubleClick += LvColors_MouseDoubleClick;
            _grpColors.Controls.AddRange(new Control[] { lblColors, _lvColors });

            // Radio CAT group to the right of Colors
            _grpCat.Text = "Radio CAT"; _grpCat.Location = new Point(400, 420); _grpCat.Size = new Size(480, 140); _grpCat.Name = "_grpCat";
            _chkCatEnabled.Text = "Enable CAT"; _chkCatEnabled.Location = new Point(15, 22); _chkCatEnabled.AutoSize = true;
            _lblCatPort = new Label { Text = "Port:", Location = new Point(120, 22), AutoSize = true };
            _cmbCatPort.DropDownStyle = ComboBoxStyle.DropDownList; _cmbCatPort.Location = new Point(165, 18); _cmbCatPort.Width = 90;
            _lblCatBaud = new Label { Text = "Baud:", Location = new Point(260, 22), AutoSize = true };
            _cmbCatBaud.DropDownStyle = ComboBoxStyle.DropDownList; _cmbCatBaud.Location = new Point(310, 18); _cmbCatBaud.Width = 80;
            _lblRig = new Label { Text = "Rig:", Location = new Point(15, 52), AutoSize = true };
            _cmbRig.DropDownStyle = ComboBoxStyle.DropDownList; _cmbRig.Location = new Point(60, 48); _cmbRig.Width = 120;
            _lblCiv = new Label { Text = "CI-V:", Location = new Point(200, 52), AutoSize = true };
            _numCiv.Minimum = 0x00; _numCiv.Maximum = 0xFF; _numCiv.Hexadecimal = true; _numCiv.Location = new Point(240, 48); _numCiv.Size = new Size(70, 23);

            _grpCat.Controls.Clear();
            _grpCat.Controls.AddRange(new Control[] { _chkCatEnabled, _lblCatPort, _cmbCatPort, _lblCatBaud, _cmbCatBaud, _lblRig, _cmbRig, _lblCiv, _numCiv });

            // CTY.DAT group below CAT
            _grpCty.Text = "CTY.DAT"; _grpCty.Location = new Point(400, 570); _grpCty.Size = new Size(480, 70); _grpCty.Name = "_grpCty";
            _chkCtyBoot.Text = "Check CTY.DAT on boot"; _chkCtyBoot.AutoSize = true; _chkCtyBoot.Location = new Point(15, 28);
            _grpCty.Controls.AddRange(new Control[] { _chkCtyBoot });

            // Appearance group (DX list font)
            _grpAppearance.Text = "Appearance"; _grpAppearance.Location = new Point(400, 650); _grpAppearance.Size = new Size(480, 100); _grpAppearance.Name = "_grpAppearance";
            _lblDxFont.Text = "DX list font:"; _lblDxFont.AutoSize = true; _lblDxFont.Location = new Point(15, 30);
            _lblDxFontSample.Text = "Sample"; _lblDxFontSample.AutoSize = true; _lblDxFontSample.Location = new Point(110, 30);
            _btnChooseDxFont.Text = "Choose..."; _btnChooseDxFont.Size = new Size(80, 27); _btnChooseDxFont.Location = new Point(380, 26);
            _btnChooseDxFont.Click += BtnChooseDxFont_Click;
            _grpAppearance.Controls.AddRange(new Control[] { _lblDxFont, _lblDxFontSample, _btnChooseDxFont });

            // Buttons bottom-right, below CAT
            _btnOk.Location = new Point(680, 760);
            _btnOk.Size = new Size(100, 27);
            _btnOk.Text = "OK";
            _btnOk.DialogResult = DialogResult.OK;
            _btnOk.Click += BtnOk_Click;

            _btnCancel.Location = new Point(790, 760);
            _btnCancel.Size = new Size(100, 27);
            _btnCancel.Text = "Cancel";
            _btnCancel.DialogResult = DialogResult.Cancel;

            // Form
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(904, 800);
            Controls.Clear();
            Controls.AddRange(new Control[] {
                _grpSources,
                _grpUser,
                _grpSpot,
                _grpColors,
                _grpCat,
                _grpCty,
                _grpAppearance,
                _btnOk,
                _btnCancel
            });
            Font = new Font("Segoe UI", 9F);
            Name = "SettingsForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Settings";
            ((System.ComponentModel.ISupportInitialize)_numGmt).EndInit();
            ((System.ComponentModel.ISupportInitialize)_numServerPort).EndInit();
            ((System.ComponentModel.ISupportInitialize)_numCiv).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private void BindSettingsToUi() {
            // Ensure there is at least one cluster and one primary marked on working copy
            try { EnsureDefaultClusterAndPrimary(); } catch { }

            // Ensure list views have columns (designer file doesn't define them)
            EnsureClusterColumns();
            EnsureColorColumns();

            // Owner draw for checkbox + Yes/No text
            _lvClusters.OwnerDraw = true;
            _lvClusters.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            // Use DrawItem to render full rows to avoid missing subitems when owner-draw is enabled
            _lvClusters.DrawItem += LvClusters_DrawItem;
            _lvClusters.MouseDown += LvClusters_MouseDown;

            // Populate clusters list
            RefreshClustersList();

            // Apply settings to spot group from working copy
            try { _numServerPort.Value = Math.Max(_numServerPort.Minimum, Math.Min(_numServerPort.Maximum, _working.LocalServerPort)); } catch { }
            _chkServerEnabled.Checked = _working.LocalServerEnabled;
            _chkDebugLog.Checked = _working.DebugLogEnabled; // initial debug logging state (default false if unset)

            // Distance units checkbox
            try { _chkUseKm.Checked = _working.UseKilometers; } catch { _chkUseKm.Checked = false; }
            try { _chkCtyBoot.Checked = _working.CheckCtyOnBoot; } catch { _chkCtyBoot.Checked = true; }

            // User profile fields
            _txtMyCall.Text = _working.MyCall;
            _txtMyCall.CharacterCasing = CharacterCasing.Upper;
            _txtName.Text = _working.Name;
            _numGmt.Value = Math.Max(_numGmt.Minimum, Math.Min(_numGmt.Maximum, _working.GmtOffsetHours));
            _txtQth.Text = _working.QTH;
            _txtGrid.Text = _working.GridSquare;
            _txtGrid.CharacterCasing = CharacterCasing.Upper;

            // Colors (list + strongly-typed model for designer/property binding)
            try { EnsureBandColors(); PopulateColorsList(); SyncDesignerColorsFromSettings(); } catch { }

            // CAT controls (populate every open)
            PopulateCatControls();
            _chkCatEnabled.Checked = _working.CatEnabled;
            _numCiv.Value = _working.IcomAddress;

            // CTY.DAT boot check
            _chkCtyBoot.Checked = _working.CheckCtyOnBoot;

            // Appearance - seed current DX list font sample
            try
            {
                var curFamily = _working.Ui?.DxListFontFamily;
                var curSize = _working.Ui?.DxListFontSize;
                var curStyle = _working.Ui?.DxListFontStyle ?? FontStyle.Regular;
                if (!string.IsNullOrWhiteSpace(curFamily) && curSize.HasValue && curSize.Value > 4f)
                {
                    _pendingDxListFont = new Font(curFamily!, curSize!.Value, curStyle, GraphicsUnit.Point);
                }
                else
                {
                    _pendingDxListFont = null;
                }
            }
            catch { _pendingDxListFont = null; }
            UpdateDxFontSampleLabel();

            // Ensure initial size fits controls in both runtime and designer
            AdjustFormSizeToContent();
        }

        private void UpdateDxFontSampleLabel()
        {
            try
            {
                var f = _pendingDxListFont ?? new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
                _lblDxFontSample.Font = f;
                _lblDxFontSample.Text = $"{f.Name}, {f.SizeInPoints:0.#}pt {f.Style}";
            }
            catch { }
        }

        private void BtnChooseDxFont_Click(object? sender, EventArgs e)
        {
            try
            {
                using var dlg = new FontDialog
                {
                    ShowEffects = false,
                    AllowScriptChange = false,
                    FontMustExist = true,
                    MinSize = 6,
                    MaxSize = 48,
                    Font = _pendingDxListFont ?? new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point)
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _pendingDxListFont = dlg.Font;
                    UpdateDxFontSampleLabel();
                }
            }
            catch { }
        }

        private void PopulateCatControls() {
            try {
                // Ports
                var ports = SerialPort.GetPortNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                _cmbCatPort.BeginUpdate();
                _cmbCatPort.Items.Clear();
                foreach (var p in ports) _cmbCatPort.Items.Add(p);
                _cmbCatPort.EndUpdate();
                if (!string.IsNullOrWhiteSpace(_working.CatPort)) {
                    int idx = _cmbCatPort.FindStringExact(_working.CatPort);
                    _cmbCatPort.SelectedIndex = idx >= 0 ? idx : (_cmbCatPort.Items.Count > 0 ? 0 : -1);
                } else {
                    _cmbCatPort.SelectedIndex = _cmbCatPort.Items.Count > 0 ? 0 : -1;
                }

                // Baud rates
                int[] bauds = new[] { 9600, 19200, 38400,57600, 115200 };
                _cmbCatBaud.BeginUpdate();
                _cmbCatBaud.Items.Clear();
                foreach (var b in bauds) _cmbCatBaud.Items.Add(b);
                _cmbCatBaud.EndUpdate();
                int bidx = _cmbCatBaud.FindStringExact(_working.CatBaud.ToString());
                _cmbCatBaud.SelectedIndex = bidx >= 0 ? bidx : (_cmbCatBaud.Items.Count > 0 ? 0 : -1);

                // Rig types
                _cmbRig.BeginUpdate();
                _cmbRig.Items.Clear();
                _cmbRig.Items.AddRange(new object[] { RigType.Icom, RigType.Yaesu, RigType.Kenwood });
                _cmbRig.EndUpdate();
                int ridx = -1;
                for (int i = 0; i < _cmbRig.Items.Count; i++) {
                    if (_cmbRig.Items[i] is RigType rt && rt == _working.Rig) { ridx = i; break; }
                }
                _cmbRig.SelectedIndex = ridx >= 0 ? ridx : 0;
            } catch { }
        }

        private void EnsureClusterColumns() {
            if (_lvClusters.Columns.Count == 0) {
                _lvClusters.Columns.Add("Name", 150);
                _lvClusters.Columns.Add("Host", 150);
                _lvClusters.Columns.Add("Port", 60);
                _lvClusters.Columns.Add("AutoLogin", 85); // space for checkbox + Yes/No
                _lvClusters.Columns.Add("Type", 90);
            }
        }

        private void EnsureColorColumns() {
            if (_lvColors.Columns.Count == 0) {
                _lvColors.Columns.Add("Band", 120);
                _lvColors.Columns.Add("Color", 220);
            }
        }

        private void AdjustFormSizeToContent() {
            try {
                const int padding = 40;
                int maxRight = 0, maxBottom = 0;
                foreach (Control c in Controls) {
                    if (!c.Visible) continue;
                    maxRight = Math.Max(maxRight, c.Right);
                    maxBottom = Math.Max(maxBottom, c.Bottom);
                }
                var desired = new Size(maxRight + padding, maxBottom + padding);
                this.ClientSize = desired;
                this.MinimumSize = new Size(desired.Width + (Width - ClientSize.Width), desired.Height + (Height - ClientSize.Height));
                // Ensure scrollbars if needed
                this.AutoScrollMinSize = desired;
            } catch { }
        }

        private void PopulateColorsList() {
            _lvColors.Items.Clear();
            foreach (var band in BandOrder) {
                if (!_working.BandColors.TryGetValue(band, out var hex)) continue;
                var it = new ListViewItem(band);
                it.SubItems.Add(hex);
                try { var c = ColorTranslator.FromHtml(hex); it.BackColor = c; it.ForeColor = c.GetBrightness() < 0.5f ? Color.White : Color.Black; } catch { }
                _lvColors.Items.Add(it);
            }
        }

        private void EnsureBandColors() {
            bool changed = false;
            foreach (var band in BandOrder) {
                if (!_working.BandColors.ContainsKey(band)) { var c = _working.ColorForBand(band); _working.SetBandColor(band, c); changed = true; }
            }
            // Do not persist while dialog is open
            if (changed) { /* delayed until OK */ }
        }

        private void EnsureDefaultClusterAndPrimary() {
            if (_working.Clusters == null) _working.Clusters = new();
            if (_working.Clusters.Count == 0) {
                var def = new ClusterDefinition { Name = "dxcluster.org", Host = "dxcluster.org", Port = 7000, AutoLogin = false, Format = ClusterFormat.DXSpider };
                _working.Clusters.Add(def);
                // Do not save here; delayed until OK
            }
        }

        private void RefreshColorsList() {
            PopulateColorsList();
            // Keep the designer model in sync if list changed elsewhere
            SyncDesignerColorsFromSettings();
        }

        // Handle color picking for a band from the list
        private void LvColors_MouseDoubleClick(object? sender, MouseEventArgs e) {
            if (_lvColors.SelectedItems.Count == 0) { MessageBox.Show(this, "Select a band first."); return; }
            var it = _lvColors.SelectedItems[0];
            var band = it.Text;
            var current = _working.BandColors.TryGetValue(band, out var hex) ? hex : "#FFFFFF";
            using var dlg = new ColorDialog();
            try { dlg.Color = ColorTranslator.FromHtml(current); } catch { }
            if (dlg.ShowDialog(this) == DialogResult.OK) {
                _working.SetBandColor(band, dlg.Color);
                var newHex = ColorTranslator.ToHtml(dlg.Color);
                it.SubItems[1].Text = newHex;
                try { it.BackColor = dlg.Color; it.ForeColor = dlg.Color.GetBrightness() < 0.5f ? Color.White : Color.Black; } catch { }
                SetDesignerColor(band, newHex);
            }
        }

        private void BtnOk_Click(object? sender, EventArgs e) => SaveAndClose();

        private void SaveAndClose() {
            // Persist colors from list view (authoritative) to working copy
            foreach (ListViewItem it in _lvColors.Items) {
                if (it.SubItems.Count < 2) continue;
                var band = it.Text;
                var hex = it.SubItems[1].Text;
                if (string.IsNullOrWhiteSpace(band) || string.IsNullOrWhiteSpace(hex)) continue;
                _working.BandColors[band] = hex.Trim();
            }

            // Spot group -> working copy
            _working.LocalServerPort = (int)_numServerPort.Value;
            _working.UseKilometers = _chkUseKm.Checked;
            _working.CheckCtyOnBoot = _chkCtyBoot.Checked;
            _working.LocalServerEnabled = _chkServerEnabled.Checked;
            _working.DebugLogEnabled = _chkDebugLog.Checked;

            // Persist user profile -> working copy
            _working.MyCall = (_txtMyCall.Text ?? string.Empty).Trim().ToUpperInvariant();
            _working.Name = _txtName.Text.Trim();
            _working.GmtOffsetHours = (int)_numGmt.Value;
            _working.QTH = _txtQth.Text.Trim();
            _working.GridSquare = (_txtGrid.Text ?? string.Empty).Trim().ToUpperInvariant();

            // Persist AutoLogin states (ensure single true) -> working copy
            bool foundTrue = false;
            foreach (ListViewItem it in _lvClusters.Items) {
                var idx = _working.Clusters.FindIndex(c => c.Name.Equals(it.Text, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) {
                    bool al = string.Equals(it.SubItems[3].Text, "Yes", StringComparison.OrdinalIgnoreCase);
                    if (al) {
                        if (!foundTrue) foundTrue = true; else { al = false; it.SubItems[3].Text = "No"; }
                    }
                    var c = _working.Clusters[idx];
                    _working.Clusters[idx] = c with { AutoLogin = al };
                }
            }

            // Persist CAT selections -> working copy
            try {
                _working.CatEnabled = _chkCatEnabled.Checked;
                _working.IcomAddress = (byte)_numCiv.Value;
                _working.CatPort = _cmbCatPort.SelectedItem as string ?? _working.CatPort;
                if (_cmbCatBaud.SelectedItem is int bi) _working.CatBaud = bi; else if (int.TryParse(_cmbCatBaud.SelectedItem?.ToString(), out var bval)) _working.CatBaud = bval;
                if (_cmbRig.SelectedItem is RigType rtSel) _working.Rig = rtSel; else if (Enum.TryParse<RigType>(_cmbRig.SelectedItem?.ToString(), out var rtParsed)) _working.Rig = rtParsed;
            } catch { }

            // Persist Appearance (DX list font) -> working copy
            try
            {
                _working.Ui ??= new UiSettings();
                if (_pendingDxListFont != null)
                {
                    _working.Ui.DxListFontFamily = _pendingDxListFont.FontFamily.Name;
                    _working.Ui.DxListFontSize = _pendingDxListFont.SizeInPoints;
                    _working.Ui.DxListFontStyle = _pendingDxListFont.Style;
                }
                else
                {
                    _working.Ui.DxListFontFamily = null;
                    _working.Ui.DxListFontSize = null;
                    _working.Ui.DxListFontStyle = FontStyle.Regular;
                }
            }
            catch { }

            // Apply working copy back to original and persist to disk
            ApplyWorkingToOriginal(_working, _settings);
            AppSettings.Save(_settings);
            DialogResult = DialogResult.OK; Close();
        }

        private void SyncDesignerColorsFromSettings() {
            // Populate strongly-typed properties from the working settings
            DesignerColors.C160m = _working.BandColors.TryGetValue("160m", out var b160) ? b160 : "#4E342E";
            DesignerColors.C80m = _working.BandColors.TryGetValue("80m", out var b80) ? b80 : "#9C27B0";
            DesignerColors.C60m = _working.BandColors.TryGetValue("60m", out var b60) ? b60 : "#009688";
            DesignerColors.C40m = _working.BandColors.TryGetValue("40m", out var b40) ? b40 : "#2196F3";
            DesignerColors.C30m = _working.BandColors.TryGetValue("30m", out var b30) ? b30 : "#00BCD4";
            DesignerColors.C20m = _working.BandColors.TryGetValue("20m", out var b20) ? b20 : "#4CAF50";
            DesignerColors.C17m = _working.BandColors.TryGetValue("17m", out var b17) ? b17 : "#8BC34A";
            DesignerColors.C15m = _working.BandColors.TryGetValue("15m", out var b15) ? b15 : "#E65100";
            DesignerColors.C12m = _working.BandColors.TryGetValue("12m", out var b12) ? b12 : "#FFEB3B";
            DesignerColors.C10m = _working.BandColors.TryGetValue("10m", out var b10) ? b10 : "#D32F2F";
            DesignerColors.C6m = _working.BandColors.TryGetValue("6m", out var b6) ? b6 : "#3F51B5";
        }

        private void SetDesignerColor(string band, string hex) {
            switch (band) {
                case "160m": DesignerColors.C160m = hex; break;
                case "80m": DesignerColors.C80m = hex; break;
                case "60m": DesignerColors.C60m = hex; break;
                case "40m": DesignerColors.C40m = hex; break;
                case "30m": DesignerColors.C30m = hex; break;
                case "20m": DesignerColors.C20m = hex; break;
                case "17m": DesignerColors.C17m = hex; break;
                case "15m": DesignerColors.C15m = hex; break;
                case "12m": DesignerColors.C12m = hex; break;
                case "10m": DesignerColors.C10m = hex; break;
                case "6m": DesignerColors.C6m = hex; break;
            }
        }

        private void RefreshClustersList() {
            try {
                _lvClusters.BeginUpdate();
                _lvClusters.Items.Clear();
                foreach (var c in _working.Clusters) {
                    var it = new ListViewItem(c.Name);
                    it.SubItems.Add(c.Host);
                    it.SubItems.Add(c.Port.ToString());
                    it.SubItems.Add(c.AutoLogin ? "Yes" : "No");
                    it.SubItems.Add(c.Format.ToString());
                    _lvClusters.Items.Add(it);
                }
                _lvClusters.Sort(); // keep Name A–Z
            } finally { _lvClusters.EndUpdate(); _lvClusters.Invalidate(); }
        }

        // Added missing event handlers
        private void LvClusters_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            var hit = _lvClusters.HitTest(e.Location);
            if (hit.Item == null) return;
            int idx = _working.Clusters.FindIndex(c => c.Name == hit.Item.Text);
            if (idx < 0) return;
            using var ed = new ClusterEditorForm(_working.Clusters[idx]);
            if (ed.ShowDialog(this) == DialogResult.OK)
            {
                var updated = ed.Def;
                if (updated.AutoLogin)
                {
                    for (int i = 0; i < _working.Clusters.Count; i++)
                        if (i != idx) _working.Clusters[i] = _working.Clusters[i] with { AutoLogin = false };
                }
                _working.Clusters[idx] = updated;
                // No save here; will persist on OK
                RefreshClustersList();
            }
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            using var ed = new ClusterEditorForm();
            if (ed.ShowDialog(this) == DialogResult.OK)
            {
                var def = ed.Def;
                if (def.AutoLogin)
                {
                    for (int i = 0; i < _working.Clusters.Count; i++)
                        _working.Clusters[i] = _working.Clusters[i] with { AutoLogin = false };
                }
                _working.Clusters.Add(def);
                // No save here; will persist on OK
                RefreshClustersList();
                _lvClusters.Sort();
            }
        }

        private void BtnEdit_Click(object? sender, EventArgs e)
        {
            if (_lvClusters.SelectedItems.Count == 0) return;
            var it = _lvClusters.SelectedItems[0];
            int idx = _working.Clusters.FindIndex(c => c.Name == it.Text);
            if (idx < 0) return;
            using var ed = new ClusterEditorForm(_working.Clusters[idx]);
            if (ed.ShowDialog(this) == DialogResult.OK)
            {
                var updated = ed.Def;
                if (updated.AutoLogin)
                {
                    for (int i = 0; i < _working.Clusters.Count; i++)
                        if (i != idx) _working.Clusters[i] = _working.Clusters[i] with { AutoLogin = false };
                }
                _working.Clusters[idx] = updated;
                // No save here; will persist on OK
                RefreshClustersList();
                _lvClusters.Sort();
            }
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (_lvClusters.SelectedItems.Count == 0) return;
            var it = _lvClusters.SelectedItems[0];
            int idx = _working.Clusters.FindIndex(c => c.Name == it.Text);
            if (idx >= 0)
            {
                _working.Clusters.RemoveAt(idx);
                // No save here; will persist on OK
                RefreshClustersList();
            }
        }

        private void LvClusters_DrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            try
            {
                var lv = e.Item.ListView;
                using var b = new SolidBrush(e.Item.Selected ? SystemColors.Highlight : (e.Item.BackColor.IsEmpty ? lv.BackColor : e.Item.BackColor));
                e.Graphics.FillRectangle(b, e.Bounds);
                int x = e.Bounds.Left;
                for (int col = 0; col < lv.Columns.Count && col < e.Item.SubItems.Count; col++)
                {
                    var cw = lv.Columns[col].Width;
                    var rect = new Rectangle(x, e.Bounds.Top, cw, e.Bounds.Height);
                    var text = e.Item.SubItems[col].Text;
                    var fore = e.Item.Selected ? SystemColors.HighlightText : SystemColors.ControlText;
                    TextRenderer.DrawText(e.Graphics, text, e.Item.Font, rect, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    x += cw;
                }
                if (e.Item.Selected) e.DrawFocusRectangle();
            }
            catch { }
        }

        private void LvClusters_MouseDown(object? sender, MouseEventArgs e)
        {
            var hit = _lvClusters.HitTest(e.Location);
            if (hit.Item == null || hit.SubItem == null) return;
            int col = hit.Item.SubItems.IndexOf(hit.SubItem);
            if (col != 3) return; // AutoLogin column
            bool newVal = !string.Equals(hit.Item.SubItems[3].Text, "Yes", StringComparison.OrdinalIgnoreCase);
            if (newVal)
            {
                foreach (ListViewItem it in _lvClusters.Items)
                    if (it != hit.Item) it.SubItems[3].Text = "No";
                for (int i = 0; i < _working.Clusters.Count; i++)
                    if (!_working.Clusters[i].Name.Equals(hit.Item.Text, StringComparison.OrdinalIgnoreCase))
                        _working.Clusters[i] = _working.Clusters[i] with { AutoLogin = false };
            }
            hit.Item.SubItems[3].Text = newVal ? "Yes" : "No";
            int idx = _working.Clusters.FindIndex(c => c.Name.Equals(hit.Item.Text, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _working.Clusters[idx] = _working.Clusters[idx] with { AutoLogin = newVal };
            }
            _lvClusters.Invalidate();
        }

        // Helpers to clone and apply settings --------------------------------------
        private static AppSettings CloneSettings(AppSettings src)
        {
            var dst = new AppSettings
            {
                // Scalars
                LastConnectedCluster = src.LastConnectedCluster,
                LocalServerPort = src.LocalServerPort,
                LocalServerEnabled = src.LocalServerEnabled,
                DebugLogEnabled = src.DebugLogEnabled,
                MyCall = src.MyCall,
                Name = src.Name,
                GmtOffsetHours = src.GmtOffsetHours,
                QTH = src.QTH,
                GridSquare = src.GridSquare,
                TrackFromUs = src.TrackFromUs,
                TrackFromDx = src.TrackFromDx,
                CatEnabled = src.CatEnabled,
                CatPort = src.CatPort,
                CatBaud = src.CatBaud,
                Rig = src.Rig,
                IcomAddress = src.IcomAddress,
                UseKilometers = src.UseKilometers,
                SettingsLeft = src.SettingsLeft,
                SettingsTop = src.SettingsTop,
                SettingsOffsetX = src.SettingsOffsetX,
                SettingsOffsetY = src.SettingsOffsetY,
                CheckCtyOnBoot = src.CheckCtyOnBoot
            };

            // Arrays
            dst.MyClusters = (src.MyClusters != null) ? src.MyClusters.ToArray() : Array.Empty<string>();
            dst.TrackBands = (src.TrackBands != null) ? src.TrackBands.ToArray() : Array.Empty<string>();

            // Collections
            dst.Clusters = src.Clusters != null ? src.Clusters.Select(c => c with { }).ToList() : new();
            dst.BandColors = src.BandColors != null ? src.BandColors.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase) : new();

            // UiSettings deep copy
            if (src.Ui != null)
            {
                dst.Ui = new UiSettings
                {
                    Left = src.Ui.Left,
                    Top = src.Ui.Top,
                    Width = src.Ui.Width,
                    Height = src.Ui.Height,
                    WindowState = src.Ui.WindowState,
                    SplitterDistance = src.Ui.SplitterDistance,
                    ColumnWidths = src.Ui.ColumnWidths != null ? src.Ui.ColumnWidths.ToArray() : Array.Empty<int>(),
                    EnabledBands = src.Ui.EnabledBands != null ? src.Ui.EnabledBands.ToArray() : Array.Empty<string>(),
                    EnabledModes = src.Ui.EnabledModes != null ? src.Ui.EnabledModes.ToArray() : new[] { "CW", "SSB", "DAT" },
                    ConsoleFontFamily = src.Ui.ConsoleFontFamily,
                    ConsoleFontSize = src.Ui.ConsoleFontSize,
                    ConsoleForeColor = src.Ui.ConsoleForeColor,
                    ConsoleBackColor = src.Ui.ConsoleBackColor,
                    DxListFontFamily = src.Ui.DxListFontFamily,
                    DxListFontSize = src.Ui.DxListFontSize,
                    DxListFontStyle = src.Ui.DxListFontStyle
                };
            }
            else dst.Ui = null;

            return dst;
        }

        private static void ApplyWorkingToOriginal(AppSettings src, AppSettings dst)
        {
            // Scalars
            dst.LastConnectedCluster = src.LastConnectedCluster;
            dst.LocalServerPort = src.LocalServerPort;
            dst.LocalServerEnabled = src.LocalServerEnabled;
            dst.DebugLogEnabled = src.DebugLogEnabled;
            dst.MyCall = src.MyCall;
            dst.Name = src.Name;
            dst.GmtOffsetHours = src.GmtOffsetHours;
            dst.QTH = src.QTH;
            dst.GridSquare = src.GridSquare;
            dst.TrackFromUs = src.TrackFromUs;
            dst.TrackFromDx = src.TrackFromDx;
            dst.CatEnabled = src.CatEnabled;
            dst.CatPort = src.CatPort;
            dst.CatBaud = src.CatBaud;
            dst.Rig = src.Rig;
            dst.IcomAddress = src.IcomAddress;
            dst.UseKilometers = src.UseKilometers;
            // Preserve SettingsLeft/Top already updated in OnFormClosing
            dst.SettingsOffsetX = src.SettingsOffsetX;
            dst.SettingsOffsetY = src.SettingsOffsetY;
            dst.CheckCtyOnBoot = src.CheckCtyOnBoot;

            // Arrays
            dst.MyClusters = (src.MyClusters != null) ? src.MyClusters.ToArray() : Array.Empty<string>();
            dst.TrackBands = (src.TrackBands != null) ? src.TrackBands.ToArray() : Array.Empty<string>();

            // Collections
            dst.Clusters = src.Clusters != null ? src.Clusters.Select(c => c with { }).ToList() : new();
            dst.BandColors = src.BandColors != null ? src.BandColors.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase) : new();

            // UiSettings deep copy
            if (src.Ui != null)
            {
                dst.Ui ??= new UiSettings();
                dst.Ui.Left = src.Ui.Left;
                dst.Ui.Top = src.Ui.Top;
                dst.Ui.Width = src.Ui.Width;
                dst.Ui.Height = src.Ui.Height;
                dst.Ui.WindowState = src.Ui.WindowState;
                dst.Ui.SplitterDistance = src.Ui.SplitterDistance;
                dst.Ui.ColumnWidths = src.Ui.ColumnWidths != null ? src.Ui.ColumnWidths.ToArray() : Array.Empty<int>();
                dst.Ui.EnabledBands = src.Ui.EnabledBands != null ? src.Ui.EnabledBands.ToArray() : Array.Empty<string>();
                dst.Ui.EnabledModes = src.Ui.EnabledModes != null ? src.Ui.EnabledModes.ToArray() : new[] { "CW", "SSB", "DAT" };
                dst.Ui.ConsoleFontFamily = src.Ui.ConsoleFontFamily;
                dst.Ui.ConsoleFontSize = src.Ui.ConsoleFontSize;
                dst.Ui.ConsoleForeColor = src.Ui.ConsoleForeColor;
                dst.Ui.ConsoleBackColor = src.Ui.ConsoleBackColor;
                dst.Ui.DxListFontFamily = src.Ui.DxListFontFamily;
                dst.Ui.DxListFontSize = src.Ui.DxListFontSize;
                dst.Ui.DxListFontStyle = src.Ui.DxListFontStyle;
            }
            else dst.Ui = null;
        }

        // Strongly-typed colors object that can be inspected/bound in a designer/property grid
        public class BandColorsModel {
            [DisplayName("160m")] public string C160m { get; set; } = "#4E342E";
            [DisplayName("80m")] public string C80m { get; set; } = "#9C27B0";
            [DisplayName("60m")] public string C60m { get; set; } = "#009688";
            [DisplayName("40m")] public string C40m { get; set; } = "#2196F3";
            [DisplayName("30m")] public string C30m { get; set; } = "#00BCD4";
            [DisplayName("20m")] public string C20m { get; set; } = "#4CAF50";
            [DisplayName("17m")] public string C17m { get; set; } = "#8BC34A";
            [DisplayName("15m")] public string C15m { get; set; } = "#E65100";
            [DisplayName("12m")] public string C12m { get; set; } = "#FFEB3B";
            [DisplayName("10m")] public string C10m { get; set; } = "#D32F2F";
            [DisplayName("6m")] public string C6m { get; set; } = "#3F51B5";
        }
    }
}
