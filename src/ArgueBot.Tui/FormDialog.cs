namespace ArgueBot.Tui
{
    using System;
    using System.Threading.Tasks;
    using TUIKit;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class FormDialog<T> : HelpDialog, IThemeable
    {
        private readonly Form _Form;
        private readonly ScrollView _Scroll;
        private readonly TuiApplication? _Application;
        private readonly Func<T, Task>? _Submit;
        private bool _Busy;
        private string _Error = "";
        private CellStyle _ErrorStyle;
        private Theme _Theme = TerminalTheme.Dark;
        /// <inheritdoc />
        public override bool CanClose { get { return !_Busy; } }
        internal FormDialog(string title, Form form, Func<T> extract, TuiApplication? application = null, Func<T, Task>? submit = null, bool readOnly = false)
        {
            if ((application == null) != (submit == null)) throw new ArgumentException("A service submit action requires a UI dispatcher.");
            _Application = application; _Submit = submit;
            Title = title; FooterHint = readOnly ? "Tab: move | Esc: back" : "Tab: move | Ctrl+S: save | Esc: cancel";
            MaxContentWidth = 76; MaxContentHeight = 24;
            _Form = form;
            if (readOnly) { _Form.SubmitLabel = "Close"; _Form.CancelLabel = "Back"; }
            _Form.ShowButtons = true; _Form.ShowFieldErrors = true;
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
            if (HandleHelp(key)) return true;
            if (key.Code == KeyCode.Escape)
            {
                if (_Form.FocusedLeaf is Dropdown<string> dropdown && dropdown.IsOpen) dropdown.HandleKey(key);
                else _Form.Cancel();
                return true;
            }
            if (_Form.SubmitKey.HasValue && KeyChord.FromKeyEvent(key).Equals(_Form.SubmitKey.Value)) { _Form.Submit(); return true; }
            _Scroll.HandleKey(key); return true;
        }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (HandleHelpMouse(mouse)) return true;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            int row = mouse.Y - ContentBounds.Y - HeaderLines(ContentBounds.Width);
            if (row < 0) return false;
            return _Scroll.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, row, mouse.Modifiers, mouse.ClickCount));
        }
        /// <inheritdoc />
        public override bool HandlePaste(string text) { if (HelpIsOpen) return true; return _Form.HandlePaste(text); }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme) { ApplyHelpTheme(theme); _Theme = theme; _Scroll.ApplyTheme(theme); BorderStyleColor = theme.Border; BackgroundStyle = theme.Text; _ErrorStyle = theme.Error; }
        private int FormHeight(int width)
        {
            _Form.Render(new LayoutProbeSurface(width));
            return Math.Max(1, _Form.ContentHeight);
        }
        private int HeaderLines(int width)
        {
            string message = _Busy ? "Saving on the service machine..." : _Error;
            return message.Length == 0 ? 0 : Math.Min(4, (message.Length + Math.Max(1, width) - 1) / Math.Max(1, width));
        }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(72, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return HelpHeight(FormHeight(Math.Max(1, contentWidth - 1)) + HeaderLines(contentWidth)); }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            if (RenderHelp(content)) return;
            _Scroll.ApplyTheme(_Theme);
            TerminalTheme.ApplyInputs(_Form, _Theme);
            int width = Math.Max(1, content.Size.Width); int lines = HeaderLines(width);
            string message = _Busy ? "Saving on the service machine..." : _Error;
            for (int row = 0; row < lines; row++)
            {
                int start = row * width;
                if (start < message.Length) content.DrawText(0, row, message.Substring(start, Math.Min(width, message.Length - start)), _ErrorStyle);
            }
            int formWidth = Math.Max(1, width - 1);
            int height = FormHeight(formWidth);
            _Scroll.SetContentSize(formWidth, height); _Scroll.Render(new SurfaceView(content, new Rect(0, lines, width, Math.Max(0, content.Size.Height - lines))));
        }
    }
}
