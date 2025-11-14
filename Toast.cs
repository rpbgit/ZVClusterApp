using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZVClusterApp.WinForms
{
    // Simple modeless toast notification that auto-closes after the given timeout
    public static class Toast
    {
        public static void Show(IWin32Window owner, string text, int timeoutMs = 5000, string? title = null, bool success = true)
        {
            var toast = new ToastForm(text, title, timeoutMs, success);
            toast.Show(owner);
        }

        private sealed class ToastForm : Form
        {
            private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer() { Interval = 2000 };
            private readonly Label _label = new() { AutoSize = true, MaximumSize = new Size(480, 0) };

            public ToastForm(string text, string? title, int timeoutMs, bool success)
            {
                Text = title ?? "Info";
                _label.Text = text;
                _label.ForeColor = success ? Color.DarkGreen : Color.DarkRed;

                _timer.Interval = Math.Max(500, timeoutMs);
                _timer.Tick += (s, e) => Close();

                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                TopMost = true;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Padding = new Padding(12);
                BackColor = SystemColors.Info;

                Controls.Add(_label);
            }

            protected override void OnShown(EventArgs e) { base.OnShown(e); _timer.Start(); }
            protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
        }
    }
}
