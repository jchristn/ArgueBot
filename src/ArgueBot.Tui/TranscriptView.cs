namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class TranscriptView : IWidget, IFocusable, IMouseAware, IThemeable, IKeyHintSource
    {
        private readonly Pane _Pane;
        private CellStyle _Background;
        private int _Height = 1;
        internal event Action? ReadRequested;
        internal TranscriptView(Pane pane) { _Pane = pane; }
        /// <inheritdoc />
        public Size Measure(Size available) { return _Pane.Measure(available); }
        /// <inheritdoc />
        public void Render(ISurface surface) { _Height = surface.Size.Height; _Pane.Render(surface, _Background); }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { _Background = theme.Text; }
        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse) { return _Pane.HandleMouse(mouse); }
        /// <inheritdoc />
        public bool HandleKey(KeyEvent key)
        {
            if (key.Modifiers != KeyModifiers.None) return false;
            switch (key.Code)
            {
                case KeyCode.Up: _Pane.ScrollUp(1); break;
                case KeyCode.Down: _Pane.ScrollDown(1); break;
                case KeyCode.PageUp: _Pane.ScrollUp(Math.Max(1, _Height - 1)); break;
                case KeyCode.PageDown: _Pane.ScrollDown(Math.Max(1, _Height - 1)); break;
                case KeyCode.Home: _Pane.ScrollUp(_Pane.ScrollbackCapacityLines); break;
                case KeyCode.End: _Pane.ScrollToBottom(); break;
                case KeyCode.Enter: ReadRequested?.Invoke(); break;
                case KeyCode.Character:
                    if (key.Rune != 'l') return false;
                    ReadRequested?.Invoke(); break;
                default: return false;
            }
            return true;
        }
        /// <inheritdoc />
        public IReadOnlyList<KeyHint>? GetKeyHints()
        {
            return new[] { KeyHint.For("enter", "Full history"), new KeyHint("Up/Down", "Scroll"), new KeyHint("PgUp/PgDn", "Page"), new KeyHint("Home/End", "First/latest"), KeyHint.For("escape", "Conversations") };
        }
    }
}
