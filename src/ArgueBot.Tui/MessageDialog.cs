namespace ArgueBot.Tui
{
    using System;
    using ArgueBot.Core.Models;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class MessageDialog : DialogModal, IThemeable, ITooltipProvider
    {
        private readonly TextEditor _Editor;
        private readonly ButtonRow _Actions = new ButtonRow();
        private bool _ActionsFocused;
        internal string Content { get { return _Editor.Text; } }
        internal MessageDialog(Message message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.ContentTruncated) throw new ArgumentException("Load the full message before opening this reader.", nameof(message));
            Title = message.Speaker + " | " + message.Kind; FooterHint = "Read only | PgUp/Dn: scroll | Tab: Close | Esc: back";
            MaxContentWidth = 80; MaxContentHeight = 22;
            _Editor = new TextEditor { Text = message.Content, IsReadOnly = true, WordWrap = true };
            _Editor.OnFocusChanged(true);
            _Actions.Add("Close", () => RequestClose(null)).TooltipText = "Close this read-only message without changing its content.";
        }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Tab) { _ActionsFocused = !_ActionsFocused; _Editor.OnFocusChanged(!_ActionsFocused); _Actions.OnFocusChanged(_ActionsFocused); return true; }
            if (_ActionsFocused) _Actions.HandleKey(key); else _Editor.HandleKey(key);
            return true;
        }
        /// <inheritdoc />
        public override bool HandlePaste(string text) { return _Editor.HandlePaste(text); }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            int localY = mouse.Y - ContentBounds.Y; int actionsY = Math.Max(0, ContentBounds.Height - 1);
            MouseEvent local = new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, localY >= actionsY ? 0 : localY, mouse.Modifiers, mouse.ClickCount);
            return localY >= actionsY ? _Actions.HandleMouse(local) : _Editor.HandleMouse(local);
        }
        /// <inheritdoc />
        public string? GetTooltip(int x, int y) { return "Read-only full message. Use arrows/PageUp/Down to scroll, Tab for Close or Escape to return."; }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { ApplyBorderTheme(theme); BorderStyleColor = theme.Border; BackgroundStyle = theme.Text; _Editor.ApplyTheme(theme); _Actions.ApplyTheme(theme); }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(76, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return 20; }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int actionY = Math.Max(0, content.Size.Height - 1);
            _Editor.Render(new SurfaceView(content, new Rect(0, 0, content.Size.Width, Math.Max(0, actionY - 1))));
            _Actions.Render(new SurfaceView(content, new Rect(0, actionY, content.Size.Width, 1)));
        }
    }
}
