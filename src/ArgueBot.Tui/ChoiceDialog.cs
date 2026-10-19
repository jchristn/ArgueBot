namespace ArgueBot.Tui
{
    using System;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class ChoiceDialog : DialogModal, IThemeable
    {
        private readonly ListView<string> _Choices = new ListView<string>();
        private readonly int _Count;
        private readonly string _Description;
        private CellStyle _TextStyle;
        internal ChoiceDialog(string title, string[] choices)
        {
            _Description = title.Length > 60 ? title : "";
            Title = _Description.Length == 0 ? title : "Choose an action"; FooterHint = "Up/Down: choose | Enter: select | Esc: back";
            MaxContentWidth = 76; MaxContentHeight = 22;
            _Count = choices.Length; _Choices.SetItems(choices); _Choices.OnFocusChanged(true);
            _Choices.ItemActivated += index => Close(index);
        }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { ApplyBorderTheme(theme); BackgroundStyle = theme.Text; _TextStyle = theme.Text; _Choices.ApplyTheme(theme); }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key) { if (HandleDismiss(key, -1)) return true; return _Choices.HandleKey(key); }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(72, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return Math.Max(1, _Count) + DescriptionRows(contentWidth); }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int offset = DescriptionRows(content.Size.Width);
            if (_Description.Length > 0)
            {
                int row = 0;
                foreach (StyledText line in TextWrapper.Wrap(Text.From(_Description, _TextStyle), Math.Max(1, content.Size.Width)))
                {
                    if (row >= content.Size.Height) break;
                    content.DrawStyledText(0, row++, line);
                }
            }
            _Choices.Render(new SurfaceView(content, new Rect(0, offset, content.Size.Width, Math.Max(0, content.Size.Height - offset))));
        }
        private int DescriptionRows(int width) { return _Description.Length == 0 ? 0 : TextWrapper.Wrap(Text.From(_Description), Math.Max(1, width)).Count + 1; }
    }
}
