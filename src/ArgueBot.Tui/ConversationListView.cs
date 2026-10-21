namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class ConversationListView : IWidget, IFocusable, IFocusAware, IMouseAware, IThemeable, ITooltipProvider, IKeyHintSource
    {
        private Theme _Theme = TerminalTheme.Dark;
        private readonly ListView<string> _List = new ListView<string>();
        internal event Action<int>? ItemActivated;
        internal event Action<string>? CommandRequested;
        internal int SelectedIndex { get { return _List.SelectedIndex; } }
        internal ConversationListView() { _List.ItemActivated += index => ItemActivated?.Invoke(index); }
        internal void SetItems(IEnumerable<string> items) { _List.SetItems(items); }
        internal void Select(int index) { _List.Select(index); }
        /// <inheritdoc />
        public Size Measure(Size available) { return _List.Measure(available); }
        /// <inheritdoc />
        public void Render(ISurface surface) { _List.Render(new ConversationListSurface(surface, _Theme)); }
        /// <inheritdoc />
        public bool HandleKey(KeyEvent key)
        {
            string? command = null;
            if (key.Modifiers == KeyModifiers.None)
            {
                if (key.Code == KeyCode.Delete) command = "d";
                else if (key.Code == KeyCode.Character && "dnbprflgeas/?".IndexOf((char)key.Rune) >= 0) command = ((char)key.Rune).ToString();
            }
            if (command == null) return _List.HandleKey(key);
            CommandRequested?.Invoke(command);
            return true;
        }
        /// <inheritdoc />
        public void OnFocusChanged(bool focused) { _List.OnFocusChanged(focused); }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { _Theme = theme; _List.ApplyTheme(theme); }
        /// <inheritdoc />
        public string? GetTooltip(int x, int y) { return "Click a conversation to open it, or use Up/Down and Enter. d opens deletion confirmation for the highlighted row; s confirms stopping its debate; b opens debate setup; / searches. F1 lists all shortcuts."; }
        /// <inheritdoc />
        public IReadOnlyList<KeyHint>? GetKeyHints()
        {
            List<KeyHint> hints = new List<KeyHint>();
            if (SelectedIndex >= 0)
            {
                hints.Add(new KeyHint("d", "Delete"));
                hints.Add(new KeyHint("s", "Stop"));
                hints.Add(new KeyHint("b", "Debate"));
                hints.Add(new KeyHint("r", "Run controls"));
                hints.Add(new KeyHint("l", "History"));
                hints.Add(new KeyHint("f", "Follow-up"));
            }
            hints.Add(new KeyHint("n", "New"));
            hints.Add(new KeyHint("/", "Search"));
            hints.Add(new KeyHint("e", "Agents"));
            hints.Add(new KeyHint("Up/Down", "Select"));
            if (SelectedIndex >= 0) hints.Add(KeyHint.For("enter", "Open"));
            return hints;
        }
        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse)
        {
            bool handled = _List.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X, mouse.Y, mouse.Modifiers, 1));
            if (handled && mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left && _List.SelectedIndex >= 0) ItemActivated?.Invoke(_List.SelectedIndex);
            return handled;
        }
    }
}
