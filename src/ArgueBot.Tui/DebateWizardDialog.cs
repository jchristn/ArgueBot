namespace ArgueBot.Tui
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class DebateWizardDialog : HelpDialog, IThemeable
    {
        private readonly DebateSetupEditor _Editor;
        private readonly TuiApplication? _Application;
        private readonly Func<CreateRunRequest, Task>? _Submit;
        private bool _Busy;
        private string _Error = "";
        private Theme _Theme = TerminalTheme.Dark;
        private readonly ReadOnlyTextView _Review = new ReadOnlyTextView();
        private readonly Wizard _Wizard;
        private readonly Form[] _Forms;
        private readonly ScrollView[] _Steps;
        internal DebateSetupEditor Editor { get { return _Editor; } }
        internal Wizard Wizard { get { return _Wizard; } }
        /// <inheritdoc />
        public override bool CanClose { get { return !_Busy; } }
        internal DebateWizardDialog(DebateSetupEditor editor, TuiApplication? application = null, Func<CreateRunRequest, Task>? submit = null)
        {
            if ((application == null) != (submit == null)) throw new ArgumentException("A submit action requires a UI dispatcher.");
            _Application = application; _Submit = submit; _Editor = editor;
            _Forms = new[] { editor.QuestionForm, editor.SpeakersForm, editor.SettingsForm };
            _Steps = new ScrollView[_Forms.Length];
            for (int index = 0; index < _Forms.Length; index++)
            {
                _Forms[index].WrapFocus = false;
                _Steps[index] = new ScrollView(_Forms[index], 72, 1) { ForwardKeys = true };
            }
            _Wizard = new Wizard(new[]
            {
                new WizardStep("Question", _Steps[0]) { Validate = () => Error(editor.QuestionForm) },
                new WizardStep("Speakers", _Steps[1]) { Validate = () => Error(editor.SpeakersForm) },
                new WizardStep("Settings", _Steps[2]) { Validate = () => Error(editor.SettingsForm) },
                new WizardStep("Review", _Review) { Validate = () => Error(editor.QuestionForm) ?? Error(editor.SpeakersForm) ?? Error(editor.SettingsForm) }
            }) { FinishLabel = "Start debate" };
            ((TextField)editor.QuestionForm.GetField(0)).Submitted += (_, _) => _Wizard.Next();
            ((TextField)editor.SettingsForm.GetField(0)).Submitted += (_, _) => editor.SettingsForm.SetFocusedField(1);
            ((TextField)editor.SettingsForm.GetField(1)).Submitted += (_, _) => _Wizard.Next();
            _Wizard.StepChanged += (_, _) => { if (_Wizard.IsLast) _Review.Text = _Editor.Review(); };
            _Wizard.Finished += (_, _) =>
            {
                if (_Busy) return;
                CreateRunRequest request = _Editor.Extract();
                if (_Submit == null) { Close(request); return; }
                _Busy = true; _Error = ""; SetFooter("Starting debate..."); _ = SubmitAsync(request);
            };
            _Wizard.Cancelled += (_, _) => RequestClose(null);
            _Wizard.OnFocusChanged(true);
            Title = editor.ExistingConversation ? "New debate in current conversation" : "New debate: guided setup";
            FooterHint = "Tab: move | Esc: cancel";
            MaxContentWidth = 76; MaxContentHeight = 22;
        }
        private async Task SubmitAsync(CreateRunRequest request)
        {
            try
            {
                await _Submit!(request).ConfigureAwait(false);
                _Application!.Post(() => { _Busy = false; Close(request); });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { _Application!.Post(() => { _Busy = false; _Error = exception.Message; SetFooter("Tab: move | Esc: cancel"); }); }
        }
        private static string? Error(Form form) { return form.ValidateAll().Select(error => error.Message).FirstOrDefault(); }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme)
        { ApplyHelpTheme(theme);
            _Theme = theme; ApplyBorderTheme(theme); BackgroundStyle = theme.Text; _Wizard.ApplyTheme(theme);
            TerminalTheme.ApplyInputs(_Editor.QuestionForm, theme); TerminalTheme.ApplyInputs(_Editor.SpeakersForm, theme); TerminalTheme.ApplyInputs(_Editor.SettingsForm, theme);
        }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleHelp(key)) return true;
            if (_Busy) return true;
            if (key.Code == KeyCode.Escape && _Wizard.FocusedLeaf is Dropdown<string> dropdown && dropdown.IsOpen) { dropdown.HandleKey(key); return true; }
            if (HandleDismiss(key, null)) return true;
            _Wizard.HandleKey(key); return true;
        }
        /// <inheritdoc />
        public override bool HandlePaste(string text) { if (HelpIsOpen) return true; return _Busy || _Wizard.HandlePaste(text); }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (HandleHelpMouse(mouse)) return true;
            if (_Busy) return true;
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            return _Wizard.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, mouse.Y - ContentBounds.Y - ErrorRows(ContentBounds.Width), mouse.Modifiers, mouse.ClickCount));
        }
        private int ErrorRows(int width) { return _Error.Length == 0 ? 0 : Math.Min(3, TextWrapper.Wrap(Text.From(_Error), Math.Max(1, width)).Count) + 1; }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(72, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return HelpHeight(18); }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            if (RenderHelp(content)) return;
            int width = Math.Max(1, content.Size.Width - 1);
            for (int index = 0; index < _Forms.Length; index++)
            {
                _Forms[index].Render(new LayoutProbeSurface(width));
                _Steps[index].SetContentSize(width, Math.Max(1, _Forms[index].ContentHeight));
            }
            if (_Busy)
            {
                content.DrawText(0, 0, "Starting debate on the service...", _Theme.Info);
                _Review.Render(new SurfaceView(content, new Rect(0, 2, content.Size.Width, Math.Max(0, content.Size.Height - 2))));
                return;
            }
            int offset = 0;
            if (_Error.Length > 0)
            {
                foreach (StyledText line in TextWrapper.Wrap(Text.From(_Error, _Theme.Error), Math.Max(1, content.Size.Width)))
                {
                    if (offset >= Math.Min(3, content.Size.Height)) break;
                    content.DrawStyledText(0, offset++, line);
                }
                offset++;
            }
            // Step changes rebuild toolkit buttons with default styles. Restore the current theme before drawing.
            _Wizard.Buttons.ApplyTheme(_Theme);
            _Wizard.Render(new SurfaceView(content, new Rect(0, offset, content.Size.Width, Math.Max(0, content.Size.Height - offset))));
        }
    }
}
