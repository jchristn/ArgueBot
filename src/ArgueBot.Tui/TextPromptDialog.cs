namespace ArgueBot.Tui
{
    using System;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class TextPromptDialog : HelpDialog, IThemeable
    {
        private readonly TextField _Field;
        internal TextPromptDialog(string title, string initial, bool masked = false)
        {
            Title = title; FooterHint = masked ? "Enter: accept | Ctrl+R: show or hide | Esc: cancel" : "Enter: accept | Esc: cancel";
            MaxContentWidth = 64;
            _Field = new TextField { Value = initial, IsFocused = true };
            if (masked) { _Field.MaskChar = '*'; _Field.RevealKey = KeyChord.Parse("ctrl+r"); }
        }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme)
        {
            ApplyHelpTheme(theme);
            ApplyBorderTheme(theme); BackgroundStyle = theme.Text;
            _Field.ApplyTheme(TerminalTheme.Inputs(theme));
        }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleHelp(key)) return true;
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Enter) { Close(_Field.Value); return true; }
            return _Field.HandleKey(key);
        }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (HandleHelpMouse(mouse)) return true;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            return _Field.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, mouse.Y - ContentBounds.Y, mouse.Modifiers, mouse.ClickCount));
        }
        /// <inheritdoc />
        public override bool HandlePaste(string text) { if (HelpIsOpen) return true; return _Field.HandlePaste(text); }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(60, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return HelpHeight(1); }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content) {
            if (RenderHelp(content)) return;
            _Field.Render(content);
        }
    }
}
