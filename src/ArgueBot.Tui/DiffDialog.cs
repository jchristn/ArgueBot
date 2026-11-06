namespace ArgueBot.Tui
{
    using System;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    /// <summary>A read-only line diff between two texts in a dialog, with optional action buttons. Returns the chosen button index, or null.</summary>
    internal sealed class DiffDialog : HelpDialog, IThemeable, ITooltipProvider
    {
        private readonly DiffView _Diff;
        private readonly ButtonRow _Actions = new ButtonRow();
        private bool _ActionsFocused;
        internal DiffDialog(string title, string oldText, string newText, params string[] actions)
        {
            ArgumentNullException.ThrowIfNull(title); ArgumentNullException.ThrowIfNull(oldText); ArgumentNullException.ThrowIfNull(newText); ArgumentNullException.ThrowIfNull(actions);
            Title = title; FooterHint = "Up/Down: scroll | Tab: buttons | Esc: back";
            MaxContentWidth = 100; MaxContentHeight = 30;
            _Diff = new DiffView(oldText, newText, null) { ShowLineNumbers = true, PageByViewport = true };
            _Diff.OnFocusChanged(true);
            for (int index = 0; index < actions.Length; index++) { int chosen = index; _Actions.Add(actions[index], () => RequestClose(chosen)); }
            _Actions.Add("Back", () => RequestClose(null));
        }
        /// <summary>Lines added in the new text.</summary>
        internal int AddedCount { get { return _Diff.AddedCount; } }
        /// <summary>Lines removed from the old text.</summary>
        internal int RemovedCount { get { return _Diff.RemovedCount; } }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleHelp(key)) return true;
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Tab) { _ActionsFocused = !_ActionsFocused; _Diff.OnFocusChanged(!_ActionsFocused); _Actions.OnFocusChanged(_ActionsFocused); return true; }
            if (_ActionsFocused) _Actions.HandleKey(key); else _Diff.HandleKey(key);
            return true;
        }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (HandleHelpMouse(mouse)) return true;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            int localY = mouse.Y - ContentBounds.Y; int actionsY = Math.Max(0, ContentBounds.Height - 1);
            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left)
            {
                _ActionsFocused = localY >= actionsY; _Diff.OnFocusChanged(!_ActionsFocused); _Actions.OnFocusChanged(_ActionsFocused);
            }
            MouseEvent local = new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, localY >= actionsY ? 0 : localY, mouse.Modifiers, mouse.ClickCount);
            return localY >= actionsY ? _Actions.HandleMouse(local) : _Diff.HandleMouse(local);
        }
        /// <inheritdoc />
        public string? GetTooltip(int x, int y) { return "Lines marked - were removed since this revision; lines marked + were added."; }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { ApplyHelpTheme(theme); ApplyBorderTheme(theme); BorderStyleColor = theme.Border; BackgroundStyle = theme.Text; _Diff.ApplyTheme(theme); _Actions.ApplyTheme(theme); }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(96, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return HelpHeight(22); }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            if (RenderHelp(content)) return;
            int actionY = Math.Max(0, content.Size.Height - 1);
            _Diff.Render(new SurfaceView(content, new Rect(0, 0, content.Size.Width, Math.Max(0, actionY - 1))));
            _Actions.Render(new SurfaceView(content, new Rect(0, actionY, content.Size.Width, 1)));
        }
    }
}
