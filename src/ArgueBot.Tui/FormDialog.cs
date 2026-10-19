namespace ArgueBot.Tui
{
    using System;
    using System.Threading.Tasks;
    using TUIKit.Hosting;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class FormDialog<T> : DialogModal, IThemeable
    {
        private readonly Form _Form;
        private readonly ScrollView _Scroll;
        private readonly TuiApplication? _Application;
        private readonly Func<T, Task>? _Submit;
        private bool _Busy;
        private string _Error = "";
        private CellStyle _ErrorStyle;
        /// <inheritdoc />
        public override bool CanClose { get { return !_Busy; } }
        internal FormDialog(string title, Form form, Func<T> extract, TuiApplication? application = null, Func<T, Task>? submit = null)
        {
            if ((application == null) != (submit == null)) throw new ArgumentException("A service submit action requires a UI dispatcher.");
            _Application = application; _Submit = submit;
            Title = title; FooterHint = "Tab: next field | Ctrl+S: save | Esc: cancel";
            MaxContentWidth = 76; MaxContentHeight = 24;
            _Form = form; _Form.ShowButtons = true; _Form.ShowFieldErrors = true;
            _Form.SubmitKey = KeyChord.Parse("ctrl+s"); _Form.CancelKey = KeyChord.Parse("escape");
            _Form.SubmitRequested += (_, _) =>
            {
                if (_Busy) return;
                T value = extract();
                if (_Submit == null) { Close(value); return; }
                _Busy = true; _Error = ""; SetFieldsEnabled(false);
                _ = SubmitAsync(value);
            };
            _Form.CancelRequested += (_, _) => RequestClose(null);
            _Scroll = new ScrollView(form, 72, 1) { ForwardKeys = true };
            _Form.OnFocusChanged(true); _Form.FocusEdge(true);
        }
        private void SetFieldsEnabled(bool enabled)
        { for (int index = 0; index < _Form.FieldCount; index++) _Form.SetFieldEnabled(index, enabled); }
        private async Task SubmitAsync(T value)
        {
            try
            {
                await _Submit!(value).ConfigureAwait(false);
                _Application!.Post(() => { _Busy = false; Close(value); });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { _Application!.Post(() => { _Busy = false; _Error = exception.Message; SetFieldsEnabled(true); }); }
        }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape) { _Form.Cancel(); return true; }
            if (_Form.SubmitKey.HasValue && KeyChord.FromKeyEvent(key).Equals(_Form.SubmitKey.Value)) { _Form.Submit(); return true; }
            _Scroll.HandleKey(key); return true;
        }
        /// <inheritdoc />
        public override bool HandlePaste(string text) { return _Form.HandlePaste(text); }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { _Scroll.ApplyTheme(theme); BorderStyleColor = theme.Border; BackgroundStyle = theme.Text; _ErrorStyle = theme.Error; }
        private int HeaderLines(int width)
        {
            string message = _Busy ? "Saving on the service machine..." : _Error;
            return message.Length == 0 ? 0 : Math.Min(4, (message.Length + Math.Max(1, width) - 1) / Math.Max(1, width));
        }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(72, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return _Form.Measure(new Size(contentWidth, 10000)).Height + HeaderLines(contentWidth); }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = Math.Max(1, content.Size.Width); int lines = HeaderLines(width);
            string message = _Busy ? "Saving on the service machine..." : _Error;
            for (int row = 0; row < lines; row++)
            {
                int start = row * width;
                if (start < message.Length) content.DrawText(0, row, message.Substring(start, Math.Min(width, message.Length - start)), _ErrorStyle);
            }
            int height = _Form.Measure(new Size(width, 10000)).Height;
            _Scroll.SetContentSize(width, height); _Scroll.Render(new SurfaceView(content, new Rect(0, lines, width, Math.Max(0, content.Size.Height - lines))));
        }
    }
}
