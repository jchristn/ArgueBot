namespace ArgueBot.Tui
{
    using System;
    using System.Linq;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class DebateWizardDialog : DialogModal, IThemeable
    {
        private readonly DebateSetupEditor _Editor;
        private readonly TextEditor _Review = new TextEditor { IsReadOnly = true, WordWrap = true };
        private readonly Wizard _Wizard;
        internal Wizard Wizard { get { return _Wizard; } }
        internal DebateWizardDialog(DebateSetupEditor editor)
        {
            _Editor = editor;
            _Wizard = new Wizard(new[]
            {
                new WizardStep("Question", editor.QuestionForm) { Validate = () => Error(editor.QuestionForm) },
                new WizardStep("Speakers", editor.SpeakersForm) { Validate = () => Error(editor.SpeakersForm) },
                new WizardStep("Settings", editor.SettingsForm) { Validate = () => Error(editor.SettingsForm) },
                new WizardStep("Review", _Review) { Validate = () => Error(editor.QuestionForm) ?? Error(editor.SpeakersForm) ?? Error(editor.SettingsForm) }
            }) { FinishLabel = "Start debate" };
            ((TextField)editor.QuestionForm.GetField(0)).Submitted += (_, _) => _Wizard.Next();
            ((TextField)editor.SettingsForm.GetField(0)).Submitted += (_, _) => editor.SettingsForm.SetFocusedField(1);
            ((TextField)editor.SettingsForm.GetField(1)).Submitted += (_, _) => _Wizard.Next();
            _Wizard.StepChanged += (_, _) => { if (_Wizard.IsLast) _Review.Text = _Editor.Review(); };
            _Wizard.Finished += (_, _) => Close(_Editor.Extract());
            _Wizard.Cancelled += (_, _) => RequestClose(null);
            _Wizard.OnFocusChanged(true);
            Title = "New debate: guided setup";
            FooterHint = "Tab: move | Enter: choose | Alt+Left/Right: Back/Next | Esc: cancel";
            MaxContentWidth = 76; MaxContentHeight = 22;
        }
        private static string? Error(Form form) { return form.ValidateAll().Select(error => error.Message).FirstOrDefault(); }
        /// <inheritdoc />
        public void ApplyTheme(Theme theme)
        {
            ApplyBorderTheme(theme); BackgroundStyle = theme.Text; _Wizard.ApplyTheme(theme);
            TerminalTheme.ApplyInputs(_Editor.QuestionForm, theme); TerminalTheme.ApplyInputs(_Editor.SpeakersForm, theme); TerminalTheme.ApplyInputs(_Editor.SettingsForm, theme);
        }
        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            _Wizard.HandleKey(key); return true;
        }
        /// <inheritdoc />
        public override bool HandlePaste(string text) { return _Wizard.HandlePaste(text); }
        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (!ContentBounds.Contains(new Point(mouse.X, mouse.Y))) return false;
            return _Wizard.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - ContentBounds.X, mouse.Y - ContentBounds.Y, mouse.Modifiers, mouse.ClickCount));
        }
        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth) { return Math.Min(72, availableWidth); }
        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth) { return 18; }
        /// <inheritdoc />
        protected override void RenderContent(ISurface content) { _Wizard.Render(content); }
    }
}
