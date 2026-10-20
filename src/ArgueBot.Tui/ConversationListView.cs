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
        private readonly ListView<string> _List = new ListView<string>();
        internal event Action<int>? ItemActivated;
        internal int SelectedIndex { get { return _List.SelectedIndex; } }
        internal ConversationListView() { _List.ItemActivated += index => ItemActivated?.Invoke(index); }
        internal void SetItems(IEnumerable<string> items) { _List.SetItems(items); }
        internal void Select(int index) { _List.Select(index); }
        /// <inheritdoc />
        public Size Measure(Size available) { return _List.Measure(available); }
        /// <inheritdoc />
        public void Render(ISurface surface) { _List.Render(surface); }
        /// <inheritdoc />
        public bool HandleKey(KeyEvent key) { return _List.HandleKey(key); }
        /// <inheritdoc />
        public void OnFocusChanged(bool focused) { _List.OnFocusChanged(focused); }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { _List.ApplyTheme(theme); }
        /// <inheritdoc />
        public string? GetTooltip(int x, int y) { return "Click a conversation to open it, or use Up/Down and Enter. Menu contains conversation actions."; }
        /// <inheritdoc />
        public IReadOnlyList<KeyHint>? GetKeyHints() { return _List.GetKeyHints(); }
        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse)
        {
            bool handled = _List.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X, mouse.Y, mouse.Modifiers, 1));
            if (handled && mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left && _List.SelectedIndex >= 0) ItemActivated?.Invoke(_List.SelectedIndex);
            return handled;
        }
    }
}
