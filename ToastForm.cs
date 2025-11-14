using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZVClusterApp.WinForms
{
    internal sealed class ToastForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly int _durationMs;
        private readonly Form? _owner;
        private readonly Color _borderColor;

        public ToastForm(string text, Color back, Color fore, int durationMs, Form? owner)
            : this(text, back, fore, Color.FromArgb(100, Color.Black), durationMs, owner)
        {
        }

        public ToastForm(string text, Color back, Color fore, Color border, int durationMs, Form? owner)
        {
            _durationMs = Math.Max(1000, durationMs);
            _owner = owner;
            _borderColor = border;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = back;
            ForeColor = fore;
            Opacity = 0.95;
            Padding = new Padding(14);

            var lbl = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(500, 0),
                Text = text,
                ForeColor = fore,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            Controls.Add(lbl);

            Load += (s, e) => PositionAndSize(lbl);

            _timer.Interval = _durationMs;
            _timer.Tick += (s, e) => { try { Close(); } catch { } };
            Shown += (s, e) => _timer.Start();
            FormClosed += (s, e) => { try { _timer.Stop(); _timer.Dispose(); } catch { } };
        }

        private void PositionAndSize(Label lbl)
        {
            try
            {
                lbl.Location = new Point(Padding.Left, Padding.Top);
                var size = TextRenderer.MeasureText(lbl.Text, lbl.Font, new Size(500, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.Left);
                var w = Math.Min(520, size.Width + Padding.Horizontal);
                var h = size.Height + Padding.Vertical;
                Size = new Size(w, h);

                Rectangle workArea;
                if (_owner != null && _owner.Visible)
                {
                    workArea = _owner.RectangleToScreen(_owner.ClientRectangle);
                }
                else
                {
                    workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
                }
                // bottom-right with margin
                int margin = 16;
                Left = workArea.Right - Width - margin;
                Top = workArea.Bottom - Height - margin;
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08; // WS_EX_TOPMOST
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(_borderColor);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            e.Graphics.DrawRectangle(pen, rect);
        }
    }
}
