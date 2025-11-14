using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ZVClusterApp.WinForms
{
    public class ClusterEditorForm : Form
    {
        public ClusterDefinition Def { get; private set; }

        private TextBox _txtName = null!;
        private TextBox _txtHost = null!;
        private NumericUpDown _numPort = null!;
        private CheckBox _chkAutoLogin = null!;
        private TextBox _txtDefaultCmds = null!;
        private Button _btnOk = null!;
        private Button _btnCancel = null!;
        private ComboBox _cmbType = null!;

        private const int MaxDefaultCommandLines = 10;

        public ClusterEditorForm(ClusterDefinition? def = null)
        {
            Def = def ?? new ClusterDefinition();
            Text = def == null ? "Add Cluster" : "Edit Cluster";
            Initialize();
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        private void Initialize()
        {
            SuspendLayout();
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;

            int xLabel = 10;
            int xInput = 140;
            int widthInput = 480; // slightly narrower so we can guarantee visible buttons
            int y = 10;
            int vgap = 8;

            // Name
            var lblName = new Label { Left = xLabel, Top = y, Width = 120, Text = "Name:" }; _txtName = new TextBox { Left = xInput, Top = y, Width = widthInput, Text = Def.Name, TabIndex = 0 };
            y += lblName.Height + vgap;

            // Host / Port
            var lblHost = new Label { Left = xLabel, Top = y, Width = 120, Text = "Host:" }; _txtHost = new TextBox { Left = xInput, Top = y, Width = 260, Text = Def.Host, TabIndex = 1 };
            var lblPort = new Label { Left = xInput + 270, Top = y, Width = 40, Text = "Port:" }; _numPort = new NumericUpDown { Left = xInput + 315, Top = y - 2, Width = 65, Minimum = 1, Maximum = 65535, Value = Def.Port, TabIndex = 2 };
            y += lblHost.Height + vgap;

            // Type
            var lblType = new Label { Left = xLabel, Top = y, Width = 120, Text = "Node Type:" }; _cmbType = new ComboBox { Left = xInput, Top = y, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 3 };
            _cmbType.Items.AddRange(new object[] { ClusterFormat.DXSpider, ClusterFormat.ARCluster, ClusterFormat.CCCluster });
            _cmbType.SelectedItem = Def.Format;
            y += lblType.Height + vgap;

            // Auto-login
            _chkAutoLogin = new CheckBox { Left = xLabel, Top = y, Width = 120, Text = "Auto-login", Checked = Def.AutoLogin, TabIndex = 4 };
            y += _chkAutoLogin.Height + vgap;

            // Default commands label
            var lblDefault = new Label { Left = xLabel, Top = y, Text = "Default commands (max 10, one per line):", AutoSize = true };
            y += lblDefault.Height + 4;

            // Multi-line default commands
            var initial = (Def.DefaultCommands != null && Def.DefaultCommands.Length > 0) ? string.Join(Environment.NewLine, Def.DefaultCommands) : string.Empty;
            _txtDefaultCmds = new TextBox
            {
                Left = xLabel,
                Top = y,
                Width = widthInput + (xInput - xLabel) - 5, // stretch to near right side
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = initial,
                AcceptsReturn = true,
                AcceptsTab = true,
                TabIndex = 5
            };
            int lineHeight = TextRenderer.MeasureText("X", _txtDefaultCmds.Font).Height + 2;
            _txtDefaultCmds.Height = lineHeight * MaxDefaultCommandLines;
            y += _txtDefaultCmds.Height + vgap;

            // OK / Cancel buttons
            _btnOk = new Button { Text = "OK", Width = 90, Height = 28, TabIndex = 6 };
            _btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel, TabIndex = 7 };
            // Position buttons flush to right with small gap
            int rightEdge = xLabel + _txtDefaultCmds.Width;
            _btnCancel.Left = rightEdge - _btnCancel.Width; _btnOk.Left = _btnCancel.Left - _btnOk.Width - 8;
            _btnOk.Top = _btnCancel.Top = y;
            _btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnOk.Click += BtnOk_Click;
            AcceptButton = _btnOk; CancelButton = _btnCancel;

            Controls.AddRange(new Control[] { lblName, _txtName, lblHost, _txtHost, lblPort, _numPort, lblType, _cmbType, _chkAutoLogin, lblDefault, _txtDefaultCmds, _btnOk, _btnCancel });

            // Final client size based on content
            int formWidth = rightEdge + 10; // small padding
            int formHeight = _btnOk.Bottom + 15; // bottom padding
            ClientSize = new Size(formWidth, formHeight);
            MinimumSize = new Size(formWidth, formHeight);

            // Ensure buttons visible on top
            _btnOk.BringToFront(); _btnCancel.BringToFront();
            ResumeLayout(false);
            PerformLayout();
        }

        private void BtnOk_Click(object? s, EventArgs e)
        {
            var fmt = _cmbType.SelectedItem is ClusterFormat cf ? cf : ClusterFormat.Auto;
            string[] defCmdText;
            if (string.IsNullOrEmpty(_txtDefaultCmds.Text))
            {
                defCmdText = Array.Empty<string>();
            }
            else
            {
                defCmdText = _txtDefaultCmds.Text
                    .Replace("\r\n", "\n")
                    .Replace("\r", "\n")
                    .Split(new[] { '\n' }, StringSplitOptions.None)
                    .Take(MaxDefaultCommandLines)
                    .ToArray();
            }

            Def = Def with
            {
                Name = _txtName.Text.Trim(),
                Host = _txtHost.Text.Trim(),
                Port = (int)_numPort.Value,
                AutoLogin = _chkAutoLogin.Checked,
                DefaultCommands = defCmdText,
                Format = fmt
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
