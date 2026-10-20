namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class ReadOnlyTextView : IWidget, IFocusable, IFocusAware, IMouseAware, IThemeable, IPasteTarget
    {
        private string _Text = "";
        private readonly List<StyledText> _Rows = new List<StyledText>();
        private int _Width = -1;
        private int _Height = 1;
        private int _Top;
        private bool _Focused;
        private CellStyle _Style;
        private CellStyle _Border;
        private CellStyle _Accent;
        internal string Text
        {
            get { return _Text; }
            set { ArgumentNullException.ThrowIfNull(value); _Text = value; _Top = 0; _Width = -1; }
        }
        /// <inheritdoc />
        public Size Measure(Size available) { return available; }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { _Style = theme.Text; _Border = theme.Border; _Accent = theme.Accent; _Width = -1; }
        /// <inheritdoc />
        public void OnFocusChanged(bool focused) { _Focused = focused; }
        private void Scroll(int top) { _Top = Math.Max(0, Math.Min(top, Math.Max(0, _Rows.Count - _Height))); }
        /// <inheritdoc />
        public bool HandleKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Up: Scroll(_Top - 1); break;
                case KeyCode.Down: Scroll(_Top + 1); break;
                case KeyCode.PageUp: Scroll(_Top - Math.Max(1, _Height - 1)); break;
                case KeyCode.PageDown: Scroll(_Top + Math.Max(1, _Height - 1)); break;
                case KeyCode.Home: Scroll(0); break;
                case KeyCode.End: Scroll(_Rows.Count); break;
            }
            return true;
        }
        /// <inheritdoc />
        public bool HandlePaste(string text) { return true; }
        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                if (mouse.Button == MouseButton.WheelUp) Scroll(_Top - 3);
                else if (mouse.Button == MouseButton.WheelDown) Scroll(_Top + 3);
                return true;
            }
            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left && mouse.X == _Width - 1)
            {
                Scroll(Math.Max(0, mouse.Y) * Math.Max(0, _Rows.Count - _Height) / Math.Max(1, _Height - 1)); return true;
            }
            return false;
        }
        /// <inheritdoc />
        public void Render(ISurface surface)
        {
            int width = surface.Size.Width; _Height = surface.Size.Height;
            if (width <= 0 || _Height <= 0) return;
            if (_Width != width)
            {
                _Width = width; _Rows.Clear();
                foreach (string line in _Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
                {
                    IReadOnlyList<StyledText> rows = TextWrapper.Wrap(TUIKit.Text.From(line, _Style), Math.Max(1, width - 1));
                    if (rows.Count == 0) _Rows.Add(StyledText.Empty); else _Rows.AddRange(rows);
                }
            }
            Scroll(_Top);
            surface.Fill(new Rect(0, 0, width, _Height), Cell.Blank(_Style));
            for (int row = 0; row < _Height && _Top + row < _Rows.Count; row++) surface.DrawStyledText(0, row, _Rows[_Top + row]);
            if (_Rows.Count > _Height)
            {
                for (int row = 0; row < _Height; row++) surface.DrawText(width - 1, row, "│", _Border);
                int thumb = Math.Max(1, _Height * _Height / _Rows.Count);
                int start = _Top * Math.Max(0, _Height - thumb) / Math.Max(1, _Rows.Count - _Height);
                for (int row = start; row < start + thumb; row++) surface.DrawText(width - 1, row, "█", _Focused ? _Accent : _Border);
            }
        }
    }
}
