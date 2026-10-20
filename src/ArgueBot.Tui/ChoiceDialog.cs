namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;

    internal sealed class ChoiceDialog : HelpDialog, IThemeable
    {
        private readonly string[] _Choices;
        private readonly string _Description;
        private readonly Dictionary<int, Rect> _HitAreas = new Dictionary<int, Rect>();
        private Theme _Theme = TerminalTheme.Dark;
        private int _Selected;
        private int _Top;
        private int _VisibleCount = 1;
        private int _ListOffset;
        private int _ListHeight;
        private bool _Overflow;
        internal ChoiceDialog(string title, string[] choices)
        {
            _Choices = choices;
            _Description = title.Length > 60 ? title : "";
            Title = _Description.Length == 0 ? title : "Choose an action";
            FooterHint = "↑↓ move | Enter select | Esc back";
            MaxContentWidth = 76; MaxContentHeight = 22;
        }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { ApplyHelpTheme(theme); _Theme = theme; ApplyBorderTheme(theme); BackgroundStyle = theme.Text; }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleHelp(key)) return true;
            if (HandleDismiss(key, -1)) return true;
            switch (key.Code)
            {
                case KeyCode.Up: Select(_Selected - 1); break;
                case KeyCode.Down: Select(_Selected + 1); break;
                case KeyCode.Home: Select(0); break;
                case KeyCode.End: Select(_Choices.Length - 1); break;
                case KeyCode.PageUp: Select(_Selected - _VisibleCount); break;
                case KeyCode.PageDown: Select(_Selected + _VisibleCount); break;
                case KeyCode.Enter: if (_Choices.Length > 0) Close(_Selected); break;
            }
            return true;
        }
        private void Select(int index) { _Selected = Math.Max(0, Math.Min(index, _Choices.Length - 1)); }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (HandleHelpMouse(mouse)) return true;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                if (mouse.Button == MouseButton.WheelUp) Select(_Selected - 1);
                else if (mouse.Button == MouseButton.WheelDown) Select(_Selected + 1);
                return true;
            }
            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left) return false;
            Point local = new Point(mouse.X - ContentBounds.X, mouse.Y - ContentBounds.Y);
            if (_Overflow && local.X == ContentBounds.Width - 1 && local.Y >= _ListOffset)
            {
                Select((local.Y - _ListOffset) * Math.Max(0, _Choices.Length - 1) / Math.Max(1, _ListHeight - 1)); return true;
            }
            foreach (KeyValuePair<int, Rect> hit in _HitAreas)
            {
                if (!hit.Value.Contains(local)) continue;
                _Selected = hit.Key; Close(_Selected); return true;
            }
            return false;
        }
        private static IReadOnlyList<StyledText> Rows(string text, int width, CellStyle style)
        { return TextWrapper.Wrap(Text.From(text, style), Math.Max(1, width)); }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(72, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            int height = DescriptionRows(contentWidth);
            foreach (string choice in _Choices) height += Math.Max(1, Rows(choice, contentWidth - 3, _Theme.Text).Count);
            return HelpHeight(Math.Max(1, height));
        }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            if (RenderHelp(content)) return;
            _HitAreas.Clear();
            int offset = DescriptionRows(content.Size.Width);
            if (_Description.Length > 0)
            {
                int row = 0;
                foreach (StyledText line in Rows(_Description, content.Size.Width, _Theme.Text))
                {
                    if (row >= content.Size.Height) break;
                    content.DrawStyledText(0, row++, line);
                }
            }
            int available = Math.Max(0, content.Size.Height - offset);
            _ListOffset = offset; _ListHeight = available;
            int totalRows = 0;
            foreach (string choice in _Choices) totalRows += Math.Max(1, Rows(choice, content.Size.Width - 3, _Theme.Text).Count);
            _Overflow = totalRows > available;
            _Top = Math.Min(_Top, _Selected);
            int selectedRows = 0;
            for (int index = _Top; index <= _Selected && index < _Choices.Length; index++) selectedRows += Math.Max(1, Rows(_Choices[index], content.Size.Width - 3, _Theme.Text).Count);
            while (_Top < _Selected && selectedRows > available)
            {
                selectedRows -= Math.Max(1, Rows(_Choices[_Top], content.Size.Width - 3, _Theme.Text).Count); _Top++;
            }
            int y = offset; _VisibleCount = 0;
            for (int index = _Top; index < _Choices.Length && y < content.Size.Height; index++)
            {
                CellStyle style = index == _Selected ? _Theme.Selection : _Theme.Text;
                IReadOnlyList<StyledText> rows = Rows(_Choices[index], content.Size.Width - 3, style);
                int naturalHeight = Math.Max(1, rows.Count);
                if (naturalHeight > content.Size.Height - y && index != _Selected) break;
                int height = Math.Min(naturalHeight, content.Size.Height - y);
                Rect area = new Rect(0, y, Math.Max(0, content.Size.Width - 1), height);
                content.Fill(area, Cell.Blank(style)); _HitAreas[index] = area; _VisibleCount++;
                content.DrawText(0, y, index == _Selected ? "> " : "  ", style);
                for (int row = 0; row < rows.Count && row < height; row++) content.DrawStyledText(2, y + row, rows[row]);
                y += height;
            }
            _VisibleCount = Math.Max(1, _VisibleCount);
            if (_Overflow && available > 0)
            {
                int before = 0;
                for (int index = 0; index < _Top; index++) before += Math.Max(1, Rows(_Choices[index], content.Size.Width - 3, _Theme.Text).Count);
                int thumb = Math.Max(1, available * available / Math.Max(1, totalRows));
                int start = before * Math.Max(0, available - thumb) / Math.Max(1, totalRows - available);
                for (int row = 0; row < available; row++) content.DrawText(content.Size.Width - 1, offset + row, row >= start && row < start + thumb ? "█" : "│", _Theme.Border);
            }
        }
        private int DescriptionRows(int width) { return _Description.Length == 0 ? 0 : Rows(_Description, width, _Theme.Text).Count + 1; }
    }
}
