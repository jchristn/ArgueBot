namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;

    /// <summary>Modal multi-select: Space toggles an option, Enter confirms. Closes with the selected indexes, or -1 when dismissed.</summary>
    internal sealed class ChecklistDialog : HelpDialog, IThemeable
    {
        private readonly string[] _Choices;
        private readonly bool[] _Checked;
        private readonly string _Description;
        private readonly int _Minimum;
        private readonly int _Maximum;
        private readonly Dictionary<int, Rect> _HitAreas = new Dictionary<int, Rect>();
        private Theme _Theme = TerminalTheme.Dark;
        private int _Selected;
        private int _Top;
        private string _Message = "";

        internal ChecklistDialog(string title, string description, string[] choices, int minimum, int maximum)
        {
            _Choices = choices; _Checked = new bool[choices.Length]; _Description = description; _Minimum = minimum; _Maximum = Math.Max(1, maximum);
            Title = title;
            FooterHint = "↑↓ move | Space toggle | Enter confirm | Esc back";
            MaxContentWidth = 76; MaxContentHeight = 22;
        }

        /// <summary>Indexes currently selected, in display order.</summary>
        internal int[] SelectedIndexes { get { return Enumerable.Range(0, _Choices.Length).Where(index => _Checked[index]).ToArray(); } }

        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { ApplyHelpTheme(theme); _Theme = theme; ApplyBorderTheme(theme); BackgroundStyle = theme.Text; }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleHelp(key)) return true;
            if (HandleDismiss(key, -1)) return true;
            switch (key.Code)
            {
                case KeyCode.Up: _Selected = Math.Max(0, _Selected - 1); break;
                case KeyCode.Down: _Selected = Math.Min(_Choices.Length - 1, _Selected + 1); break;
                case KeyCode.Home: _Selected = 0; break;
                case KeyCode.End: _Selected = _Choices.Length - 1; break;
                case KeyCode.Enter: Confirm(); break;
                case KeyCode.Character: if (key.Rune == ' ') Toggle(_Selected); break;
            }
            return true;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (HandleHelpMouse(mouse)) return true;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left) return false;
            Point local = new Point(mouse.X - ContentBounds.X, mouse.Y - ContentBounds.Y);
            foreach (KeyValuePair<int, Rect> hit in _HitAreas)
            {
                if (!hit.Value.Contains(local)) continue;
                _Selected = hit.Key; Toggle(hit.Key); return true;
            }
            return false;
        }

        private void Toggle(int index)
        {
            if (index < 0 || index >= _Choices.Length) return;
            if (!_Checked[index] && SelectedIndexes.Length >= _Maximum) { _Message = "Choose at most " + _Maximum + "."; return; }
            _Checked[index] = !_Checked[index]; _Message = "";
        }

        private void Confirm()
        {
            int count = SelectedIndexes.Length;
            if (count < _Minimum) { _Message = "Choose at least " + _Minimum + "."; return; }
            Close(SelectedIndexes);
        }

        private static IReadOnlyList<StyledText> Rows(string text, int width, CellStyle style) { return TextWrapper.Wrap(Text.From(text, style), Math.Max(1, width)); }

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(72, availableWidth); }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            int height = Rows(_Description, contentWidth, _Theme.Text).Count + 3 + _Choices.Length;
            return HelpHeight(Math.Max(1, height));
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            if (RenderHelp(content)) return;
            _HitAreas.Clear();
            int y = 0;
            foreach (StyledText line in Rows(_Description, content.Size.Width, _Theme.Text))
            {
                if (y >= content.Size.Height) return;
                content.DrawStyledText(0, y++, line);
            }
            y++;
            int available = Math.Max(1, content.Size.Height - y - 2);
            if (_Selected < _Top) _Top = _Selected;
            if (_Selected >= _Top + available) _Top = _Selected - available + 1;
            for (int index = _Top; index < _Choices.Length && y < content.Size.Height - 2; index++)
            {
                CellStyle style = index == _Selected ? _Theme.Selection : _Theme.Text;
                Rect area = new Rect(0, y, Math.Max(0, content.Size.Width - 1), 1);
                content.Fill(area, Cell.Blank(style)); _HitAreas[index] = area;
                string row = (index == _Selected ? "> " : "  ") + (_Checked[index] ? "[x] " : "[ ] ") + _Choices[index];
                content.DrawText(0, y, row.Length > content.Size.Width ? row.Substring(0, content.Size.Width) : row, style);
                y++;
            }
            string status = _Message.Length > 0 ? _Message : SelectedIndexes.Length + " selected";
            if (content.Size.Height > 0) content.DrawText(0, content.Size.Height - 1, status.Length > content.Size.Width ? status.Substring(0, content.Size.Width) : status, _Message.Length > 0 ? _Theme.Accent : _Theme.Muted);
        }
    }
}
