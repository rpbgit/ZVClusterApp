using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ZVClusterApp.WinForms
{
    internal sealed class FavoritesEditForm : Form
    {
        private readonly TextBox _txt;
        private readonly Button _btnOk;
        private readonly Button _btnCancel;
        private readonly Label _lblHelp;

        public List<string> Patterns { get; private set; } = new();

        // If you have the editorFont overload, keep it and set CharacterCasing=Upper in both ctors
        public FavoritesEditForm(IEnumerable<string> existing, Font editorFont)
        {
            Text = "Edit DX Filter (DX Calls)";
            StartPosition = FormStartPosition.CenterParent;
            Width = 420;
            Height = 430;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _txt = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = editorFont ?? SystemFonts.MessageBoxFont,
                AcceptsReturn = true,
                AcceptsTab = false,
                CharacterCasing = CharacterCasing.Upper // enforce uppercase input
            };
            _txt.Text = string.Join(Environment.NewLine, (existing ?? Array.Empty<string>()).Select(s => (s ?? string.Empty).ToUpperInvariant()));

            _lblHelp = new Label
            {
                Dock = DockStyle.Top,
                Height = 72,
                Text = "One pattern per line.\nUppercase only. Supports * (any chars) and ? (single char), e.g. 9U1*, VK0EK, FT?, KH?ABC.\nEmpty lines or lines starting with # are ignored.",
                TextAlign = ContentAlignment.MiddleLeft
            };

            _btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
            _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
                Padding = new Padding(8)
            };
            bottom.Controls.AddRange(new Control[] { _btnOk, _btnCancel });

            Controls.Add(_txt);
            Controls.Add(bottom);
            Controls.Add(_lblHelp);

            _btnOk.Click += (s, e) => { Patterns = ParsePatterns(_txt.Text); };
        }

        // Back-compat ctor
        public FavoritesEditForm(IEnumerable<string> existing)
            : this(existing, SystemFonts.MessageBoxFont)
        {
        }

        private static List<string> ParsePatterns(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;

            foreach (var line in raw.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var t = (line ?? string.Empty).Trim().ToUpperInvariant(); // normalize to UPPER
                if (t.Length == 0) continue;
                if (t.StartsWith("#")) continue;
                // allow only A-Z 0-9 / - * ?
                if (!Regex.IsMatch(t, @"^[A-Z0-9/\-\*\?]+$")) continue;
                list.Add(t);
            }

            return list
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }
    }
}