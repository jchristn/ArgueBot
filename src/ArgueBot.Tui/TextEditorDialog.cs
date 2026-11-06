namespace ArgueBot.Tui
{
    using System;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    /// <summary>A multi-line text editor in a dialog. Ctrl+S or Save returns the text; Escape or Cancel returns null.</summary>
    internal sealed class TextEditorDialog : HelpDialog, IThemeable, ITooltipProvider
    {
        private static readonly KeyChord _Save = KeyChord.Parse("ctrl+s");
        private readonly TextEditor _Editor;
        private readonly ButtonRow _Actions = new ButtonRow();
        private bool _ActionsFocused;
        internal string Text { get { return _Editor.Text; } }
        internal TextEditorDialog(string title, string text, string saveLabel = "Save")
        {
            ArgumentNullException.ThrowIfNull(title); ArgumentNullException.ThrowIfNull(text);
            Title = title; FooterHint = "Ctrl+S: save | Tab: buttons | Esc: cancel";
            MaxContentWidth = 100; MaxContentHeight = 30;
            _Editor = new TextEditor { Text = text, WordWrap = true, IsFocused = true, ConsumeUnboundControlKeys = false };
            _Actions.Add(saveLabel, () => RequestClose(_Editor.Text)).TooltipText = "Save the text (Ctrl+S).";
            _Actions.Add("Cancel", () => RequestClose(null)).TooltipText = "Close without saving.";
        }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleHelp(key)) return true;
            if (KeyChord.FromKeyEvent(key).Equals(_Save)) { RequestClose(_Editor.Text); return true; }
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Tab) { _ActionsFocused = !_ActionsFocused; _Editor.OnFocusChanged(!_ActionsFocused); _Actions.OnFocusChanged(_ActionsFocused); return true; }
            if (_ActionsFocused) _Actions.HandleKey(key); else _Editor.HandleKey(key);
            return true;
        }
        /// <inheritdoc />
        public override bool HandlePaste(string text) { if (HelpIsOpen) return true; return _ActionsFocused || _Editor.HandlePaste(text); }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (HandleHelpMouse(mouse)) return true;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            int localY = mouse.Y - ContentBounds.Y; int actionsY = Math.Max(0, ContentBounds.Height - 1);
            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left)
            {
                _ActionsFocused = localY >= actionsY; _Editor.OnFocusChanged(!_ActionsFocused); _Actions.OnFocusChanged(_ActionsFocused);
            }
            MouseEvent local = new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, localY >= actionsY ? 0 : localY, mouse.Modifiers, mouse.ClickCount);
            return localY >= actionsY ? _Actions.HandleMouse(local) : _Editor.HandleMouse(local);
        }
        /// <inheritdoc />
        public string? GetTooltip(int x, int y) { return "Edit the text. Ctrl+S saves, Tab reaches the buttons and Escape cancels."; }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { ApplyHelpTheme(theme); ApplyBorderTheme(theme); BorderStyleColor = theme.Border; BackgroundStyle = theme.Text; _Editor.ApplyTheme(TerminalTheme.Inputs(theme)); _Actions.ApplyTheme(theme); }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(96, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return HelpHeight(24); }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            if (RenderHelp(content)) return;
            int actionY = Math.Max(0, content.Size.Height - 1);
            _Editor.Render(new SurfaceView(content, new Rect(0, 0, content.Size.Width, Math.Max(0, actionY - 1))));
            _Actions.Render(new SurfaceView(content, new Rect(0, actionY, content.Size.Width, 1)));
        }
    }
}
