#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Tui;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Layout;
    using TUIKit.Terminal;
    using TUIKit.Testing;
    using TUIKit.Theming;
    using TUIKit.Widgets;
    using Touchstone.Core;

    /// <summary>Headless tests render and drive the actual terminal application shell.</summary>
    public static class TerminalSuites
    {
        /// <summary>Terminal descriptors run on the executable's .NET 10 target.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Terminal", "Headless terminal", new List<TestCaseDescriptor> { new TestCaseDescriptor("Terminal", "Navigation", "Single-click navigation ignores stale history and preserves per-conversation drafts", NavigationAsync), new TestCaseDescriptor("Terminal", "SubmitLifecycle", "Review preserves failed submissions and blocks duplicate starts or dismissal while pending", SubmitLifecycleAsync), new TestCaseDescriptor("Terminal", "MenuWrapping", "Menu labels wrap fully at narrow widths, scroll by option and retain click identity", MenuWrappingAsync), new TestCaseDescriptor("Terminal", "FirstUse", "Visible menu guides a fresh user through agent presets and review before service submission", FirstUseAsync), new TestCaseDescriptor("Terminal", "DebateSetup", "Debate wizard preserves drafts, validates steps and reviews speaker order before submission", DebateSetupAsync), new TestCaseDescriptor("Terminal", "Palette", "Quiet Charcoal uses consistent inputs, padded content and a borderless workspace", PaletteAsync), new TestCaseDescriptor("Terminal", "Transcript", "Transcript separates speakers and literal content while retaining exceptional states", TranscriptAsync), new TestCaseDescriptor("Terminal", "Splash", "Lowercase splash overlays workspace and consumes keys or closes after three seconds", SplashAsync), new TestCaseDescriptor("Terminal", "RoleForms", "Role forms clone protected definitions, validate names and preserve revision/activity", RoleFormsAsync), new TestCaseDescriptor("Terminal", "UserForms", "User forms mask passwords, preserve administrative boundaries and validate drafts", UserFormsAsync), new TestCaseDescriptor("Terminal", "MessageReader", "Full-message reader is read-only, scrolls long Unicode text and keeps a close action visible", MessageReaderAsync), new TestCaseDescriptor("Terminal", "AttemptDetails", "Attempt inspector preserves ordered sources and scrolls through read-only provenance", AttemptDetailsAsync), new TestCaseDescriptor("Terminal", "AuditDetails", "Audit detail remains read-only through paste and scrolls to its close actions", AuditDetailsAsync), new TestCaseDescriptor("Terminal", "RecoveryForms", "Recovery forms mask passphrases and reject unsafe restore destinations", RecoveryFormsAsync), new TestCaseDescriptor("Terminal", "ParticipantForms", "Participant forms preserve empty arguments, validate required fields and fit terminal sizes", ParticipantFormsAsync), new TestCaseDescriptor("Terminal", "Shell", "Responsive shell renders, traverses focus and toggles theme", ShellAsync) }) }; }
        }
        private static async Task NavigationAsync(CancellationToken token)
        {
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            TerminalNavigationHandler service = new TerminalNavigationHandler();
            using (ArgueBotClient client = new ArgueBotClient("http://127.0.0.1:4317", service))
            using (TuiApplication app = new TuiApplication(backend))
            {
                ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start();
                Task refresh = workspace.RefreshAsync(token);
                await PumpUntilAsync(app, () => refresh.IsCompleted, token).ConfigureAwait(false); await refresh.ConfigureAwait(false);
                service.DelayNextRead = true;
                Task oldRead = workspace.RefreshAsync(token);
                await PumpUntilAsync(app, () => service.ReadStarted.Task.IsCompleted, token).ConfigureAwait(false);
                ClickText(app, backend, "Beta");
                await PumpUntilAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Opening Beta", StringComparison.Ordinal), token).ConfigureAwait(false);
                service.ReleaseRead.TrySetResult();
                await PumpUntilAsync(app, () => oldRead.IsCompleted, token).ConfigureAwait(false); await oldRead.ConfigureAwait(false);
                if (Snapshot.ToText(app.CaptureFrame()!).Contains("History for Alpha", StringComparison.Ordinal)) throw new InvalidDataException("An old response replaced the newly opened conversation.");
                refresh = workspace.RefreshAsync(token);
                await PumpUntilAsync(app, () => refresh.IsCompleted, token).ConfigureAwait(false); await refresh.ConfigureAwait(false);
                if (!Snapshot.ToText(app.CaptureFrame()!).Contains("History for Beta", StringComparison.Ordinal)) throw new InvalidDataException("Single-click navigation did not open the chosen history.");
                app.Focus("composer"); backend.FeedInput(Encoding.UTF8.GetBytes("Draft for Beta")); app.PumpInputOnce(); app.RenderOnce();
                ClickText(app, backend, "Alpha");
                await PumpUntilAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Opening Alpha", StringComparison.Ordinal), token).ConfigureAwait(false);
                app.Focus("composer"); backend.FeedInput(Encoding.UTF8.GetBytes("Draft for Alpha")); app.PumpInputOnce(); app.RenderOnce();
                ClickText(app, backend, "Beta");
                await PumpUntilAsync(app, () => Snapshot.ToText(app.CaptureFrame()!).Contains("Opening Beta", StringComparison.Ordinal), token).ConfigureAwait(false);
                if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Draft for Beta", StringComparison.Ordinal)) throw new InvalidDataException("Switching conversations lost or mixed their note drafts.");
                app.Stop();
            }
        }
        private static async Task SubmitLifecycleAsync(CancellationToken token)
        {
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            using (TuiApplication app = new TuiApplication(backend))
            {
                app.Theme = TerminalTheme.Dark; app.ApplyThemeToWidgets = true; app.Start();
                List<RunnerProfile> profiles = new List<RunnerProfile> { AgentPresets.Create(0), AgentPresets.Create(1) };
                profiles[0].Id = "fixture-one"; profiles[1].Id = "fixture-two";
                DebateSetupEditor editor = new DebateSetupEditor(profiles, "Keep this question after a failed start", false);
                TaskCompletionSource pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                List<string> commands = new List<string>();
                DebateWizardDialog dialog = new DebateWizardDialog(editor, app, request => { commands.Add(request.IdempotencyKey); return commands.Count == 1 ? pending.Task : Task.CompletedTask; });
                Task<CreateRunRequest?> result = app.ShowAsync<CreateRunRequest>(dialog);
                dialog.Wizard.Next(); dialog.Wizard.Next(); dialog.Wizard.Next(); dialog.Wizard.Next(); app.RenderOnce();
                if (dialog.CanClose || dialog.RequestClose(null)) throw new InvalidDataException("A pending start could discard its state.");
                dialog.Wizard.Next();
                if (commands.Count != 1) throw new InvalidDataException("A pending start was submitted twice.");
                pending.SetException(new InvalidOperationException("The service is temporarily unavailable."));
                await PumpUntilAsync(app, () => dialog.CanClose && ContentText(dialog, app.CaptureFrame()!).Contains("temporarilyunavailable", StringComparison.Ordinal), token).ConfigureAwait(false);
                if (result.IsCompleted || dialog.Wizard.CurrentIndex != 3 || ((TextField)editor.QuestionForm.GetField(0)).Value != "Keep this question after a failed start") throw new InvalidDataException("A failed start discarded its review or draft.");
                await SaveEvidenceAsync("debate-start-error", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                ClickText(app, backend, "[ Start debate ]");
                await PumpUntilAsync(app, () => result.IsCompleted, token).ConfigureAwait(false);
                if (await result.ConfigureAwait(false) == null || commands.Count != 2 || commands[0] != commands[1]) throw new InvalidDataException("Explicit retry lost its command identity.");
                app.Stop();
            }
        }
        private static async Task MenuWrappingAsync(CancellationToken token)
        {
            string[] topics = new[] { "Navigation and conversations", "Configure agents and harnesses", "Start and control a debate", "History, messages and attempts", "Users, roles, audit and recovery", "Theme and detaching" };
            foreach (int width in new[] { 40, 60, 80, 120 })
            {
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.ApplyThemeToWidgets = true; app.Theme = TerminalTheme.Dark; app.Start();
                    ChoiceDialog dialog = new ChoiceDialog("Help", topics);
                    Task<object?> result = app.ShowAsync(dialog); app.RenderOnce();
                    CellBuffer frame = app.CaptureFrame()!;
                    StringBuilder text = new StringBuilder(); Rect bounds = dialog.ContentBounds;
                    for (int row = bounds.Y; row < bounds.Y + bounds.Height; row++)
                        for (int column = bounds.X; column < bounds.X + bounds.Width; column++) text.Append(frame.Get(column, row).Grapheme);
                    string visible = string.Concat(text.ToString().Where(character => !char.IsWhiteSpace(character)));
                    foreach (string topic in topics)
                        if (!visible.Contains(string.Concat(topic.Where(character => !char.IsWhiteSpace(character))), StringComparison.Ordinal)) throw new InvalidDataException("A help option was clipped at width " + width + ": " + topic);
                    await SaveEvidenceAsync("help-wrapped-" + width, Snapshot.ToText(frame), token).ConfigureAwait(false);
                    ClickText(app, backend, "Users,");
                    await PumpUntilAsync(app, () => result.IsCompleted, token).ConfigureAwait(false);
                    if (await result.ConfigureAwait(false) is not int selected || selected != 4) throw new InvalidDataException("Wrapped help text selected the wrong topic.");
                    string[] longList = Enumerable.Range(0, 20).Select(index => "Option " + index + ": this full description needs to remain readable").ToArray();
                    ChoiceDialog scrolling = new ChoiceDialog("Actions", longList);
                    Task<object?> scrollResult = app.ShowAsync(scrolling); app.RenderOnce();
                    backend.FeedKey("end"); app.PumpInputOnce(); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Option 19:", StringComparison.Ordinal)) throw new InvalidDataException("Wrapped menu did not reveal its last option.");
                    Rect scrollbar = scrolling.ContentBounds;
                    backend.FeedMouse(new MouseEvent(MouseEventKind.Press, MouseButton.Left, scrollbar.X + scrollbar.Width - 1, scrollbar.Y, KeyModifiers.None, 1)); app.PumpInputOnce(); app.RenderOnce();
                    if (scrollResult.IsCompleted) throw new InvalidDataException("Clicking a menu scrollbar activated an option.");
                    backend.FeedKey("end"); app.PumpInputOnce();
                    backend.FeedKey("enter"); app.PumpInputOnce();
                    if (await scrollResult.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) is not int last || last != 19) throw new InvalidDataException("Wrapped menu lost keyboard selection identity.");
                    app.Stop();
                }
            }
        }
        private static async Task FirstUseAsync(CancellationToken token)
        {
            RunnerProfile mux = new ParticipantEditor(AgentPresets.Create(3)).Extract();
            if (mux.Provider != "mux" || mux.Kind != "cli" || mux.Executable != "mux" || mux.Arguments.Count != 0) throw new InvalidDataException("Mux preset did not retain its executable, protocol and default arguments through the agent form.");
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            TerminalJourneyHandler service = new TerminalJourneyHandler();
            using (ArgueBotClient client = new ArgueBotClient("http://127.0.0.1:4317", service))
            using (TuiApplication app = new TuiApplication(backend))
            {
                ConversationApp workspace = new ConversationApp(client); workspace.Configure(app); app.Start(); app.RenderOnce();
                string[] rows = Snapshot.ToText(app.CaptureFrame()!).Split('\n');
                int menuColumn = rows[1].IndexOf("Menu", StringComparison.Ordinal);
                if (menuColumn < 0) throw new InvalidDataException("The main menu was not visible.");
                ClickText(app, backend, "Menu");
                await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Main menu", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                ClickText(app, backend, "New debate or conversation");
                await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("New:", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                ClickText(app, backend, "Start a debate: guided setup");
                for (int agent = 0; agent < 2; agent++)
                {
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Set up agents", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                    ClickText(app, backend, "Add or enable an agent");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Agents:", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                    ClickText(app, backend, "Add agent: CLI harness or REST model");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title == "Add agent: choose how it runs", token).ConfigureAwait(false);
                    ClickText(app, backend, agent == 0 ? "Claude Code (installed CLI)" : "Codex (installed CLI)");
                    await PumpUntilAsync(app, () => app.Modals.Top is FormDialog<RunnerProfile>, token).ConfigureAwait(false);
                    for (int field = 0; field < 40; field++)
                    {
                        app.RenderOnce();
                        if (Snapshot.ToText(app.CaptureFrame()!).Contains("[ Save ]", StringComparison.Ordinal)) break;
                        backend.FeedKey("tab"); app.PumpInputOnce();
                    }
                    ClickText(app, backend, "[ Save ]");
                    int count = agent + 1;
                    await PumpUntilAsync(app, () => service.Profiles.Count == count, token).ConfigureAwait(false);
                }
                await PumpUntilAsync(app, () => app.Modals.Top is DebateWizardDialog, token).ConfigureAwait(false);
                DebateWizardDialog dialog = (DebateWizardDialog)app.Modals.Top!;
                Form question = dialog.Editor.QuestionForm;
                ((TextField)question.GetField(0)).Value = "Which architecture should we use?";
                backend.FeedKey("enter");
                await PumpUntilAsync(app, () => dialog.Wizard.CurrentIndex == 1, token).ConfigureAwait(false);
                backend.FeedKey("alt+right");
                await PumpUntilAsync(app, () => dialog.Wizard.CurrentIndex == 2, token).ConfigureAwait(false);
                backend.FeedKey("alt+right");
                await PumpUntilAsync(app, () => dialog.Wizard.CurrentIndex == 3, token).ConfigureAwait(false);
                if (service.Conversation != null || service.Started != null) throw new InvalidDataException("Setup wrote a conversation or started agents before review confirmation.");
                await SaveEvidenceAsync("first-use-review", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                ClickText(app, backend, "[ Start debate ]");
                await PumpUntilAsync(app, () => service.Started != null, token).ConfigureAwait(false);
                if (service.Conversation?.Title != "Which architecture should we use?" || service.Started?.ParticipantIds.Count != 2 || service.Profiles[0].Provider != "claude" || service.Profiles[1].Provider != "codex") throw new InvalidDataException("First-use setup lost its question or selected agent presets.");
                await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    ClickText(app, backend, "Menu");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title?.StartsWith("Main menu", StringComparison.Ordinal) == true, token).ConfigureAwait(false);
                    ClickText(app, backend, "Delete this conversation");
                    await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog menu && menu.Title == "Choose an action", token).ConfigureAwait(false);
                    string confirmation = ContentText((DialogModal)app.Modals.Top!, app.CaptureFrame()!);
                    if (!confirmation.Contains("cannotbeundone", StringComparison.Ordinal)) throw new InvalidDataException("Delete confirmation did not explain permanent removal.");
                    service.RejectDelete = attempt == 1;
                    ClickText(app, backend, attempt == 0 ? "Cancel: keep this conversation" : "Delete permanently");
                    if (attempt == 1)
                    {
                        await PumpUntilAsync(app, () => app.Modals.Top is ChoiceDialog error && error.Title == "Choose an action", token).ConfigureAwait(false);
                        if (service.Deleted) throw new InvalidDataException("An active-run refusal was ignored.");
                        backend.FeedKey("escape");
                    }
                    await PumpUntilAsync(app, () => !workspace.IsBusy, token).ConfigureAwait(false);
                    if (attempt < 2 && service.Deleted) throw new InvalidDataException("Conversation was deleted after cancel or an active-run refusal.");
                }
                if (!service.Deleted) throw new InvalidDataException("Confirmed stopped conversation deletion was not submitted.");
                app.Stop();
            }
        }
        private static string ContentText(DialogModal dialog, CellBuffer frame)
        {
            StringBuilder text = new StringBuilder(); Rect bounds = dialog.ContentBounds;
            for (int row = bounds.Y; row < bounds.Y + bounds.Height; row++)
                for (int column = bounds.X; column < bounds.X + bounds.Width; column++) text.Append(frame.Get(column, row).Grapheme);
            return string.Concat(text.ToString().Where(character => !char.IsWhiteSpace(character)));
        }
        private static void ClickText(TuiApplication app, HeadlessBackend backend, string text)
        {
            app.RenderOnce(); string[] rows = Snapshot.ToText(app.CaptureFrame()!).Split('\n');
            int row = Array.FindIndex(rows, line => line.Contains(text, StringComparison.Ordinal));
            if (row < 0) throw new InvalidDataException("Expected a visible clickable action: " + text);
            int column = rows[row].IndexOf(text, StringComparison.Ordinal) + 1;
            backend.FeedMouse(new MouseEvent(MouseEventKind.Press, MouseButton.Left, column, row, KeyModifiers.None, 1));
            backend.FeedMouse(new MouseEvent(MouseEventKind.Release, MouseButton.Left, column, row, KeyModifiers.None, 1));
        }
        private static async Task PumpUntilAsync(TuiApplication app, Func<bool> condition, CancellationToken token)
        {
            for (int index = 0; index < 200; index++)
            {
                token.ThrowIfCancellationRequested(); app.PumpInputOnce(); app.RenderOnce();
                if (condition()) return;
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("Terminal journey did not reach the expected screen. Current modal: " + (app.Modals.Top is DialogModal dialog ? dialog.Title : "none"));
        }
        private static async Task DebateSetupAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { TerminalTheme.Dark, Theme.Light })
            {
                HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.ApplyThemeToWidgets = true; app.Start();
                    List<RunnerProfile> profiles = new List<RunnerProfile> { new RunnerProfile { Id = "one", Name = "Claude" }, new RunnerProfile { Id = "two", Name = "Codex" }, new RunnerProfile { Id = "three", Name = "Gemini" } };
                    DebateSetupEditor editor = new DebateSetupEditor(profiles, "", false);
                    DebateWizardDialog dialog = new DebateWizardDialog(editor);
                    Task<CreateRunRequest?> result = app.ShowAsync<CreateRunRequest>(dialog);
                    if (dialog.Wizard.Next()) throw new InvalidDataException("The wizard accepted an empty question.");
                    ((TextField)editor.QuestionForm.GetField(0)).Value = "Compare these approaches";
                    app.RenderOnce();
                    await SaveEvidenceAsync("debate-question-" + theme.Name, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    backend.FeedKey("f1"); app.PumpInputOnce(); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal)) throw new InvalidDataException("F1 did not provide help inside the wizard.");
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal), token).ConfigureAwait(false);
                    if (dialog.IsClosed || ((TextField)editor.QuestionForm.GetField(0)).Value != "Compare these approaches") throw new InvalidDataException("Context help lost the debate draft.");
                    backend.FeedKey("enter"); app.PumpInputOnce();
                    if (dialog.Wizard.CurrentIndex != 1) throw new InvalidDataException("Enter did not advance from the question.");
                    ((Dropdown<string>)editor.SpeakersForm.GetField(1)).SelectedIndex = 0;
                    if (dialog.Wizard.Next()) throw new InvalidDataException("Duplicate speakers were accepted.");
                    ((Dropdown<string>)editor.SpeakersForm.GetField(1)).SelectedIndex = 1;
                    ((Dropdown<string>)editor.SpeakersForm.GetField(2)).SelectedIndex = 3;
                    dialog.Wizard.Next(); dialog.Wizard.Back(); app.RenderOnce();
                    editor.SpeakersForm.SetFocusedField(2); backend.FeedKey("enter"); app.PumpInputOnce(); app.RenderOnce();
                    if (!((Dropdown<string>)editor.SpeakersForm.GetField(2)).IsOpen) throw new InvalidDataException("The final-agent dropdown did not open.");
                    await SaveEvidenceAsync("debate-expanded-speakers-" + theme.Name, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !((Dropdown<string>)editor.SpeakersForm.GetField(2)).IsOpen, token).ConfigureAwait(false);
                    if (dialog.IsClosed || ((Dropdown<string>)editor.SpeakersForm.GetField(2)).IsOpen) throw new InvalidDataException("Escape discarded the wizard instead of closing the dropdown.");
                    string speakers = Snapshot.ToText(app.CaptureFrame()!);
                    if (!speakers.Contains("Next", StringComparison.Ordinal) || !speakers.Contains("Cancel", StringComparison.Ordinal) || !speakers.Contains("Third agent", StringComparison.Ordinal)) throw new InvalidDataException("Speaker choices or navigation were hidden.");
                    await SaveEvidenceAsync("debate-speakers-" + theme.Name, speakers, token).ConfigureAwait(false);
                    dialog.Wizard.Next();
                    ((TextField)editor.SettingsForm.GetField(0)).Value = "51";
                    if (dialog.Wizard.Next()) throw new InvalidDataException("An invalid round limit was accepted.");
                    ((TextField)editor.SettingsForm.GetField(0)).Value = "6";
                    app.RenderOnce();
                    await SaveEvidenceAsync("debate-settings-" + theme.Name, Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                    dialog.Wizard.Back();
                    if (((Dropdown<string>)editor.SpeakersForm.GetField(2)).SelectedIndex != 3) throw new InvalidDataException("Back lost the speaker selection.");
                    dialog.Wizard.Next(); dialog.Wizard.Next(); app.RenderOnce();
                    string review = Snapshot.ToText(app.CaptureFrame()!);
                    if (!review.Contains("Start debate", StringComparison.Ordinal) || !review.Contains("Closes each round: Gemini", StringComparison.Ordinal)) throw new InvalidDataException("Review omitted the ending speaker or Start action.");
                    await SaveEvidenceAsync("debate-review-" + theme.Name, review, token).ConfigureAwait(false);
                    if (result.IsCompleted) throw new InvalidDataException("The debate started before the review was confirmed.");
                    dialog.Wizard.Next();
                    CreateRunRequest? request = await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (request == null || request.Question != "Compare these approaches" || !request.ParticipantIds.SequenceEqual(new[] { "one", "two", "three" }) || request.MaxRounds != 6 || request.InterventionSeconds != 15) throw new InvalidDataException("Debate setup changed the prompt, speaker order or settings.");
                    app.Stop();
                }
            }
        }
        private static async Task PaletteAsync(CancellationToken token)
        {
            HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
            using (ArgueBotClient client = new ArgueBotClient())
            using (TuiApplication app = new TuiApplication(backend))
            {
                new ConversationApp(client).Configure(app); app.Start(); app.RenderOnce();
                CellBuffer frame = app.CaptureFrame()!;
                Color charcoal = Color.FromRgb(0x17, 0x19, 0x1C); Color input = Color.FromRgb(0x25, 0x28, 0x2D);
                if (!frame.Get(30, 5).Style.Background.Equals(charcoal) || !frame.Get(2, 20).Style.Background.Equals(input)) throw new InvalidDataException("Workspace and input backgrounds do not use the shared palette.");
                foreach (int column in new[] { 0, 79 })
                {
                    if (frame.Get(column, 20).Grapheme != " ") throw new InvalidDataException("Composer or sidebar content touches its frame.");
                }
                if (Snapshot.ToText(frame).IndexOfAny(new[] { '┌', '┐', '└', '┘', '┏', '┓', '┗', '┛' }) >= 0) throw new InvalidDataException("The workspace reintroduced rectangular pane frames.");
                if (frame.Get(23, 10).Grapheme != "│") throw new InvalidDataException("The sidebar divider disappeared.");
                CellBuffer listBuffer = new CellBuffer(30, 3);
                ConversationListView list = new ConversationListView(); list.ApplyTheme(TerminalTheme.Dark);
                list.SetItems(new[] { "議論 with Mux" }); list.Select(0); list.OnFocusChanged(true); list.Render(new BufferSurface(listBuffer));
                if (!listBuffer.Get(0, 0).Style.Equals(TerminalTheme.Dark.Selection) || !Snapshot.ToText(listBuffer).Contains("議論 with Mux", StringComparison.Ordinal)) throw new InvalidDataException("Conversation selection lost its quiet palette or wide Unicode text.");
                await SaveEvidenceAsync("palette-workspace", Snapshot.ToText(frame), token).ConfigureAwait(false);
                string footer = Snapshot.ToText(frame).Split('\n').Last().Trim();
                if (!footer.StartsWith("F1 Help", StringComparison.Ordinal)) throw new InvalidDataException("Help must lead the terminal footer.");
                backend.FeedKey("f1"); app.PumpInputOnce();
                for (int index = 0; index < 30 && !app.Modals.IsActive; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                app.RenderOnce();
                string helpScreen = Snapshot.ToText(app.CaptureFrame()!);
                if (!helpScreen.Contains("Configure agents and harnesses", StringComparison.Ordinal)) throw new InvalidDataException("F1 did not open the help topic menu.");
                await SaveEvidenceAsync("help-menu", helpScreen, token).ConfigureAwait(false);
                backend.FeedKey("escape");
                for (int index = 0; index < 30 && app.Modals.IsActive; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                if (app.Modals.IsActive) throw new InvalidDataException("Help menu did not close on Escape.");
                Task<string?> prompt = app.PromptStyledAsync("New conversation", "A question"); app.RenderOnce();
                if (app.Modals.Top is not TextPromptDialog) throw new InvalidDataException("A prompt bypassed the shared dialog style.");
                await SaveEvidenceAsync("palette-prompt", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                backend.FeedKey("enter");
                for (int index = 0; index < 30 && !prompt.IsCompleted; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                if (await prompt.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != "A question") throw new InvalidDataException("Styled prompt changed its initial value.");
                Task<int> choice = app.SelectStyledAsync("Participants", "First", "Second");
                backend.FeedKey("down"); backend.FeedKey("enter"); app.PumpInputOnce(); app.PumpInputOnce();
                if (await choice.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != 1) throw new InvalidDataException("Styled choice lost keyboard selection.");
                ParticipantEditor editor = new ParticipantEditor();
                FormDialog<RunnerProfile> dialog = new FormDialog<RunnerProfile>("Participant", editor.Form, editor.Extract);
                Task<object?> form = app.ShowAsync(dialog); app.RenderOnce();
                if (!((TextField)editor.Form.GetField(0)).NormalStyle.Background.Equals(input)) throw new InvalidDataException("Form fields bypassed the shared input background.");
                await SaveEvidenceAsync("palette-participant", Snapshot.ToText(app.CaptureFrame()!), token).ConfigureAwait(false);
                backend.FeedKey("escape");
                for (int index = 0; index < 30 && !form.IsCompleted; index++) { app.PumpInputOnce(); await Task.Delay(10, token).ConfigureAwait(false); }
                await form.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                app.Stop();
            }
        }
        private static async Task TranscriptAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Dark, Theme.Light })
            {
                HeadlessBackend backend = new HeadlessBackend(80, 30) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme;
                    Pane pane = app.AddPane("transcript", region => region.FillWidth().FillHeight());
                    List<Message> messages = new List<Message>
                    {
                        new Message { Speaker = "user", Content = "Compare the two approaches. [red] stays literal." },
                        new Message { Speaker = "Claude", Kind = "answer", Content = "First, consider the trade-offs.\n\nKeep the explanation readable." },
                        new Message { Speaker = "Codex", Kind = "answer", State = "interrupted", Content = "Partial answer", ContentTruncated = true }
                    };
                    TranscriptFormatter.Render(pane, messages, theme);
                    app.Start(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("You", StringComparison.Ordinal) || !screen.Contains("Claude", StringComparison.Ordinal) || !screen.Contains("Interrupted", StringComparison.Ordinal) || !screen.Contains("[red] stays literal", StringComparison.Ordinal) || !screen.Contains("Ctrl+L", StringComparison.Ordinal) || screen.Contains("completed", StringComparison.Ordinal) || screen.Contains("user | note", StringComparison.Ordinal) || screen.Contains("trade-offs.Keep", StringComparison.Ordinal)) throw new InvalidDataException("Transcript lost readable headings, original content or exceptional-state notices.");
                    string[] transcriptRows = screen.Split('\n');
                    int userRow = Array.FindIndex(transcriptRows, row => row.Trim() == "You");
                    int agentRow = Array.FindIndex(transcriptRows, row => row.Trim() == "Claude");
                    if (userRow < 0 || agentRow < 0 || !transcriptRows[userRow + 1].Contains("Compare the two approaches", StringComparison.Ordinal) || !transcriptRows[agentRow + 1].Contains("First, consider", StringComparison.Ordinal)) throw new InvalidDataException("Message headings must be immediately followed by their content.");
                    if (!TranscriptFormatter.Heading(new Message { Kind = "steer" }).Contains("Private directive", StringComparison.Ordinal)) throw new InvalidDataException("Private directive label disappeared.");
                    await SaveEvidenceAsync("transcript-" + theme.Name, screen, token).ConfigureAwait(false);
                    app.Stop();
                }
            }
        }
        private static async Task SplashAsync(CancellationToken token)
        {
            foreach (bool pressKey in new[] { true, false })
            {
                HeadlessBackend backend = new HeadlessBackend(80, 24) { ClaimsTerminal = false };
                using (ArgueBotClient client = new ArgueBotClient())
                using (TuiApplication app = new TuiApplication(backend))
                {
                    new ConversationApp(client).Configure(app); app.Start();
                    Task splash = SplashScreen.ShowAsync(app, token);
                    app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("Ctrl+Q Detach", StringComparison.Ordinal) || !screen.Contains("|___/", StringComparison.Ordinal) || !screen.Contains("Press any key", StringComparison.Ordinal) || !screen.Contains("v0.1.0", StringComparison.Ordinal) || !screen.Contains("2026 Joel Christner", StringComparison.Ordinal) || !screen.Contains("https://github.com/jchristn/ArgueBot", StringComparison.Ordinal) || !screen.Contains("MIT License", StringComparison.Ordinal) || !screen.Contains("Ctrl+E: configure agents", StringComparison.Ordinal)) throw new InvalidDataException("Splash did not preserve its lowercase logo and surrounding workspace.");
                    string[] rows = screen.Split('\n');
                    int hintRow = Array.FindIndex(rows, row => row.Contains("Press any key", StringComparison.Ordinal));
                    if (hintRow < 0 || !rows[hintRow + 1].Contains(new string(' ', 62), StringComparison.Ordinal)) throw new InvalidDataException("Splash needs an empty interior row below its final text.");
                    foreach (string group in new[] { "Let your agents", "https://github.com", "Ctrl+E: configure" })
                    {
                        int row = Array.FindIndex(rows, line => line.Contains(group, StringComparison.Ordinal));
                        if (row <= 0 || !rows[row - 1].Contains(new string(' ', 62), StringComparison.Ordinal)) throw new InvalidDataException("Splash text groups lost their spacing.");
                    }
                    await SaveEvidenceAsync("splash", screen, token).ConfigureAwait(false);
                    if (pressKey) { backend.FeedKey("ctrl+n"); app.PumpInputOnce(); }
                    else
                    {
                        await Task.Delay(2800, token).ConfigureAwait(false);
                        if (splash.IsCompleted) throw new InvalidDataException("Splash closed before three seconds.");
                        await Task.Delay(400, token).ConfigureAwait(false);
                        app.PumpInputOnce();
                    }
                    await splash.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    app.RenderOnce();
                    if (app.Modals.IsActive) throw new InvalidDataException("Splash dismissal activated a workspace shortcut or left its modal open.");
                    app.Stop();
                }
            }
        }
        private static async Task RoleFormsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Dark, Theme.Light })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    RoleRecord source = new RoleRecord { Id = "rol_fixture", Name = "Viewer", Builtin = true, Protected = true };
                    bool protectedRole = false;
                    try { new RoleEditor(source); } catch (ArgumentException) { protectedRole = true; }
                    if (!protectedRole) throw new InvalidDataException("Protected role entered a terminal edit form.");
                    RoleEditor editor = new RoleEditor(source, true);
                    FormDialog<RoleRequest> dialog = new FormDialog<RoleRequest>("Clone role: Viewer", editor.Form, editor.Extract); dialog.ApplyTheme(theme);
                    Task<RoleRequest?> result = app.ShowAsync<RoleRequest>(dialog);
                    ((TextField)editor.Form.GetField(0)).Value = "";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (dialog.IsClosed) throw new InvalidDataException("An empty role name closed its draft.");
                    ((TextField)editor.Form.GetField(0)).Value = "Terminal custom 議論";
                    editor.Form.SetFocusedField(1); backend.FeedKey("space"); app.PumpInputOnce(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("Role enabled", StringComparison.Ordinal) || !screen.Contains("Save", StringComparison.Ordinal)) throw new InvalidDataException("Terminal role form lost its enabled control or save action.");
                    await SaveEvidenceAsync("role-editor-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    RoleRequest? request = await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (request == null || request.CloneRoleId != source.Id || request.ExpectedRevision != 0 || request.Active) throw new InvalidDataException("Terminal clone changed source binding, revision or selected activity.");
                    RoleEditor update = new RoleEditor(new RoleRecord { Name = "Existing", Active = false, Revision = 7 });
                    if (update.Extract().ExpectedRevision != 7 || update.Extract().CloneRoleId.Length != 0 || update.Extract().Active) throw new InvalidDataException("Terminal role update lost its revision or became a clone.");
                    app.Stop();
                }
            }
        }
        private static async Task UserFormsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Dark, Theme.Light })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    UserEditor editor = new UserEditor(false, new UserRecord { Name = "Terminal identity", Email = "terminal@example.invalid", Role = "viewer", Active = true, Revision = 7 });
                    FormDialog<UserRequest> dialog = new FormDialog<UserRequest>("Edit tenant user", editor.Form, editor.Extract); dialog.ApplyTheme(theme);
                    Task<UserRequest?> result = app.ShowAsync<UserRequest>(dialog);
                    if (editor.Form.FieldCount != 6) throw new InvalidDataException("Tenant user editor exposed the system administrator flag.");
                    ((TextField)editor.Form.GetField(1)).Value = "invalid email";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (dialog.IsClosed) throw new InvalidDataException("Invalid identity metadata closed its draft.");
                    ((TextField)editor.Form.GetField(1)).Value = "terminal@example.invalid";
                    ((TextField)editor.Form.GetField(5)).Value = "fictional-terminal-password";
                    editor.Form.SetFocusedField(5); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (screen.Contains("fictional-terminal-password", StringComparison.Ordinal)) throw new InvalidDataException("The terminal rendered an unmasked login password.");
                    await SaveEvidenceAsync("user-editor-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    UserRequest? request = await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (request == null || request.PasswordSha256?.Length != 64 || request.IsAdmin || request.ExpectedRevision != 7 || request.Email != "terminal@example.invalid") throw new InvalidDataException("The terminal identity form changed privileges, revision or password representation.");
                    app.Stop();
                }
            }
        }
        private static async Task MessageReaderAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    string text = string.Join("\n", Enumerable.Range(1, 300).Select(index => "Read-only Unicode line 議論🙂 " + index)) + "\nCOMPLETE MESSAGE TAIL";
                    MessageDialog dialog = new MessageDialog(new Message { Content = text });
                    Task<Message?> result = app.ShowAsync<Message>(dialog);
                    dialog.HandlePaste("attempted mutation"); app.RenderOnce();
                    string first = Snapshot.ToText(app.CaptureFrame()!);
                    if (!first.Contains("議論🙂 1", StringComparison.Ordinal) || first.Contains("COMPLETE MESSAGE TAIL", StringComparison.Ordinal)) throw new InvalidDataException("A full message did not open at its beginning.");
                    backend.FeedKey("pagedown"); app.PumpInputOnce(); app.RenderOnce();
                    if (Snapshot.ToText(app.CaptureFrame()!) == first) throw new InvalidDataException("PageDown did not scroll the read-only message.");
                    backend.FeedKey("ctrl+end"); app.PumpInputOnce(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (dialog.Content != text || !screen.Contains("COMPLETE MESSAGE TAIL", StringComparison.Ordinal) || !screen.Contains("Close", StringComparison.Ordinal)) throw new InvalidDataException("A long full message became editable, lost its tail or hid the close action.");
                    await SaveEvidenceAsync("message-reader-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("tab"); backend.FeedKey("enter"); app.PumpInputOnce(); app.PumpInputOnce();
                    if (await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != null) throw new InvalidDataException("Closing the reader returned a mutation.");
                    app.Stop();
                }
            }
        }
        private static async Task AttemptDetailsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    GenerationAttempt attempt = new GenerationAttempt { Id = "att_fixture", PromptVersion = "debate-v1", PromptDigest = new string('a', 64), State = "interrupted", ContextMessageIds = Enumerable.Range(1, 40).Select(index => "msg_fixture_" + index).ToList() };
                    AttemptDetails details = new AttemptDetails(attempt, new DebateRun { Question = "Immutable question 議論" });
                    FormDialog<GenerationAttempt> dialog = new FormDialog<GenerationAttempt>("Generation provenance", details.Form, () => attempt, readOnly: true);
                    Task<GenerationAttempt?> result = app.ShowAsync<GenerationAttempt>(dialog);
                    dialog.HandlePaste("attempted edit");
                    if (((TextField)details.Form.GetField(0)).Value != attempt.Id || ((TextField)details.Form.GetField(9)).Value != "msg_fixture_1" || ((TextField)details.Form.GetField(details.Form.FieldCount - 1)).Value != "msg_fixture_40") throw new InvalidDataException("Attempt provenance became editable or lost source ordering.");
                    for (int index = 0; index < details.Form.FieldCount + 1; index++) { backend.FeedKey("tab"); app.PumpInputOnce(); }
                    app.RenderOnce(); string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("msg_fixture_40", StringComparison.Ordinal) || !screen.Contains("Close", StringComparison.Ordinal)) throw new InvalidDataException("Long provenance did not scroll to its final source and close controls.");
                    await SaveEvidenceAsync("attempt-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("escape"); app.PumpInputOnce(); app.PumpInputOnce();
                    if (await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != null) throw new InvalidDataException("Read-only provenance inspection returned a mutation.");
                    app.Stop();
                }
            }
        }
        private static async Task AuditDetailsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(60, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    AuditEntry entry = new AuditEntry { Id = "aud_fixture", Operation = "session.issue", Outcome = "issued", TenantId = "ten_local", UserId = "usr_local", SessionId = "ses_fixture" };
                    AuditDetails details = new AuditDetails(entry);
                    FormDialog<AuditEntry> dialog = new FormDialog<AuditEntry>("Immutable security event", details.Form, () => entry, readOnly: true);
                    Task<AuditEntry?> result = app.ShowAsync<AuditEntry>(dialog);
                    dialog.HandlePaste("attempted edit");
                    if (((TextField)details.Form.GetField(0)).Value != entry.Id || !((TextField)details.Form.GetField(0)).IsReadOnly) throw new InvalidDataException("Security accounting could be edited in its inspector.");
                    for (int index = 0; index < details.Form.FieldCount + 1; index++) { backend.FeedKey("tab"); app.PumpInputOnce(); }
                    app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (!screen.Contains("Close", StringComparison.Ordinal) || !screen.Contains("Back", StringComparison.Ordinal) || screen.Contains("Save", StringComparison.Ordinal)) throw new InvalidDataException("Read-only inspection did not expose clear close actions.");
                    await SaveEvidenceAsync("audit-" + theme.Name, screen, token).ConfigureAwait(false);
                    backend.FeedKey("escape"); app.PumpInputOnce(); app.PumpInputOnce();
                    if (await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false) != null) throw new InvalidDataException("Closing read-only inspection returned a mutation.");
                    app.Stop();
                }
            }
        }
        private static async Task SaveEvidenceAsync(string name, string content, CancellationToken token)
        {
            string? folder = Environment.GetEnvironmentVariable("ARGUEBOT_TUI_EVIDENCE_DIR");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, name + ".txt"), content, token).ConfigureAwait(false);
        }
        private static async Task RecoveryFormsAsync(CancellationToken token)
        {
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                HeadlessBackend backend = new HeadlessBackend(70, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    RecoveryEditor editor = new RecoveryEditor("restore", true);
                    FormDialog<BackupRequest> dialog = new FormDialog<BackupRequest>("Restore archive", editor.Form, editor.Extract);
                    Task<BackupRequest?> result = app.ShowAsync<BackupRequest>(dialog);
                    ((TextField)editor.Form.GetField(0)).Value = "fixture-recovery-passphrase";
                    ((TextField)editor.Form.GetField(1)).Value = "../unsafe-home";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.RenderOnce();
                    if (dialog.IsClosed) throw new InvalidDataException("The restore form accepted a traversing destination.");
                    string screen = Snapshot.ToText(app.CaptureFrame()!);
                    if (screen.Contains("fixture-recovery-passphrase", StringComparison.Ordinal)) throw new InvalidDataException("The recovery form rendered a plaintext passphrase.");
                    await SaveEvidenceAsync("recovery-" + theme.Name, screen, token).ConfigureAwait(false);
                    ((TextField)editor.Form.GetField(1)).Value = "recovered_fixture";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    BackupRequest? request = await result.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (request?.DestinationName != "recovered_fixture" || request.Password != "fixture-recovery-passphrase" || request.Port != 4537) throw new InvalidDataException("The recovery form changed its explicit destination, passphrase or port.");
                    RecoveryEditor postgresql = new RecoveryEditor("restore");
                    ((TextField)postgresql.Form.GetField(1)).Value = "postgresql_fixture";
                    ((Dropdown<string>)postgresql.Form.GetField(3)).SelectedIndex = 1;
                    FormDialog<BackupRequest> providerDialog = new FormDialog<BackupRequest>("PostgreSQL restore", postgresql.Form, postgresql.Extract);
                    Task<BackupRequest?> providerResult = app.ShowAsync<BackupRequest>(providerDialog);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (providerDialog.IsClosed) throw new InvalidDataException("PostgreSQL restore accepted a missing connection reference.");
                    ((TextField)postgresql.Form.GetField(4)).Value = "ARGUEBOT_FIXTURE_RECOVERY_DATABASE";
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    BackupRequest? providerRequest = await providerResult.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (providerRequest?.RestoreDatabaseType != ArgueBot.Core.Database.DatabaseTypeEnum.Postgresql || providerRequest.RestoreConnectionEnvironmentVariable != "ARGUEBOT_FIXTURE_RECOVERY_DATABASE") throw new InvalidDataException("Terminal recovery lost the explicit PostgreSQL destination reference.");
                    app.Stop();
                }
            }
        }
        private static async Task ParticipantFormsAsync(CancellationToken token)
        {
            foreach (int width in new int[] { 60, 100 })
            foreach (Theme theme in new Theme[] { Theme.Dark, Theme.Light })
            {
                token.ThrowIfCancellationRequested();
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (TuiApplication app = new TuiApplication(backend))
                {
                    app.Theme = theme; app.Start();
                    RunnerProfile original = new RunnerProfile { Name = "Terminal 議論", Kind = "cli", Provider = "plain", Executable = "fixture", Arguments = new List<string> { "", "--directory", "space and Unicode 議論" } };
                    ParticipantEditor editor = new ParticipantEditor(original);
                    FormDialog<RunnerProfile> dialog = new FormDialog<RunnerProfile>("Edit participant", editor.Form, editor.Extract);
                    dialog.ApplyTheme(theme);
                    Task<RunnerProfile?> outcome = app.ShowAsync<RunnerProfile>(dialog);
                    editor.Form.SetFocusedField(1); backend.FeedKey("enter"); app.PumpInputOnce(); app.RenderOnce();
                    if (!((Dropdown<string>)editor.Form.GetField(1)).IsOpen) throw new InvalidDataException("The agent runner dropdown did not open.");
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !((Dropdown<string>)editor.Form.GetField(1)).IsOpen, token).ConfigureAwait(false);
                    if (dialog.IsClosed) throw new InvalidDataException("Escape discarded the agent draft instead of closing its dropdown.");
                    backend.FeedKey("f1"); app.PumpInputOnce(); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal)) throw new InvalidDataException("An agent form did not offer context help.");
                    backend.FeedKey("escape");
                    await PumpUntilAsync(app, () => !Snapshot.ToText(app.CaptureFrame()!).Contains("Back to dialog", StringComparison.Ordinal), token).ConfigureAwait(false);
                    app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame() ?? throw new InvalidDataException("Participant dialog frame was missing."));
                    await SaveEvidenceAsync("participants-" + width + "-" + theme.Name, screen, token).ConfigureAwait(false);
                    if (!screen.Contains("Edit participant", StringComparison.Ordinal) || !screen.Contains("Name", StringComparison.Ordinal)) throw new InvalidDataException("The participant form did not render its title and fields.");
                    editor.Form.SetFocusedField(editor.Form.FieldCount - 1);
                    backend.FeedKey("tab"); app.PumpInputOnce(); app.RenderOnce();
                    string bottom = Snapshot.ToText(app.CaptureFrame()!);
                    if (!bottom.Contains("Save", StringComparison.Ordinal) || !bottom.Contains("Cancel", StringComparison.Ordinal)) throw new InvalidDataException("Tab could not scroll the participant form's action buttons into view.");
                    await SaveEvidenceAsync("participants-bottom-" + width + "-" + theme.Name, bottom, token).ConfigureAwait(false);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce();
                    if (!dialog.IsClosed) throw new InvalidDataException("Save did not close the valid participant form: " + string.Join("; ", Enumerable.Range(0, editor.Form.FieldCount).Select(index => editor.Form.GetFieldError(index)).Where(error => error != null)));
                    RunnerProfile? submitted = await outcome.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (submitted == null || !submitted.Arguments.SequenceEqual(original.Arguments)) throw new InvalidDataException("Terminal form submission changed literal argument order or dropped an empty argument.");
                    ParticipantEditor retained = new ParticipantEditor(original); int attempts = 0;
                    FormDialog<RunnerProfile> retry = new FormDialog<RunnerProfile>("Retry participant", retained.Form, retained.Extract, app, candidate =>
                    {
                        attempts++;
                        if (attempts == 1) throw new InvalidOperationException("Service rejected the fixture; edit and retry.");
                        return Task.CompletedTask;
                    });
                    Task<RunnerProfile?> saved = app.ShowAsync<RunnerProfile>(retry);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.PumpInputOnce(); app.RenderOnce();
                    if (retry.IsClosed || !retained.Extract().Arguments.SequenceEqual(original.Arguments)) throw new InvalidDataException("A service save failure closed the form or discarded its draft.");
                    string rejected = Snapshot.ToText(app.CaptureFrame()!);
                    if (!rejected.Contains("Service rejected", StringComparison.Ordinal)) throw new InvalidDataException("The form did not display its service failure inline.");
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.PumpInputOnce();
                    await saved.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (attempts != 2) throw new InvalidDataException("Retry did not submit the retained participant draft exactly once.");
                    ParticipantEditor blank = new ParticipantEditor();
                    FormDialog<RunnerProfile> invalid = new FormDialog<RunnerProfile>("Create participant", blank.Form, blank.Extract);
                    Task<RunnerProfile?> validation = app.ShowAsync<RunnerProfile>(invalid);
                    backend.FeedKey("ctrl+s"); app.PumpInputOnce(); app.RenderOnce();
                    if (validation.IsCompleted) throw new InvalidDataException("A blank participant form bypassed required-field validation.");
                    backend.FeedKey("escape"); app.PumpInputOnce(); app.PumpInputOnce();
                    RunnerProfile? cancelled = await validation.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    if (cancelled != null) throw new InvalidDataException("Escape did not cancel the participant form.");
                    app.Stop();
                }
            }
        }
        private static async Task ShellAsync(CancellationToken token)
        {
            foreach (int width in new int[] { 60, 80, 120 })
            {
                token.ThrowIfCancellationRequested();
                HeadlessBackend backend = new HeadlessBackend(width, 24) { ClaimsTerminal = false };
                using (ArgueBotClient client = new ArgueBotClient())
                using (TuiApplication app = new TuiApplication(backend))
                {
                    new ConversationApp(client).Configure(app);
                    app.Start(); app.RenderOnce();
                    string screen = Snapshot.ToText(app.CaptureFrame() ?? throw new InvalidDataException("Terminal frame was missing."));
                    await SaveEvidenceAsync("workspace-" + width, screen, token).ConfigureAwait(false);
                    foreach (string label in new[] { "Conversations", "Note: Enter sends", "Welcome to ArgueBot", "Run controls", "Start debate" })
                        if (!screen.Contains(label, StringComparison.Ordinal)) throw new InvalidDataException("Workspace at " + width + " columns clipped " + label);
                    // The toolkit audit recognizes focus frames, not the borderless control indicators verified below.
                    FocusAudit.Run(app, new FocusAuditOptions { CheckFocusIndicator = false }).ThrowIfProblems();
                    app.Focus("actions"); app.RenderOnce();
                    CellBuffer actionFocus = app.CaptureFrame()!;
                    if (!Enumerable.Range(0, width * 4).Any(offset => actionFocus.Get(offset % width, offset / width).Style.Background.Equals(app.Theme.Selection.Background))) throw new InvalidDataException("Toolbar buttons lost their local focus highlight.");
                    foreach (string region in new[] { "threads", "composer" })
                    {
                        app.Focus(region); app.RenderOnce();
                        string focusedScreen = Snapshot.ToText(app.CaptureFrame()!);
                        if (!focusedScreen.Contains(region == "threads" ? "› Conversations" : "› Note: Enter sends", StringComparison.Ordinal)) throw new InvalidDataException("A borderless workspace control lost its focus indicator.");
                        if (!Snapshot.ToText(app.CaptureFrame()!).Split('\n').Last().Trim().StartsWith("F1 Help", StringComparison.Ordinal)) throw new InvalidDataException("Help lost the first footer position when focus moved.");
                    }
                    backend.FeedKey("ctrl+t"); app.PumpInputOnce(); app.RenderOnce();
                    if (app.Theme.Name != "Light") throw new InvalidDataException("Theme shortcut was not delivered.");
                    backend.FeedKey("ctrl+t"); app.PumpInputOnce(); app.RenderOnce();
                    if (app.Theme.Name != "Dark") throw new InvalidDataException("Theme shortcut did not toggle back.");
                    backend.Resize(40, 16); app.RenderOnce();
                    if (!Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal too small", StringComparison.Ordinal)) throw new InvalidDataException("A small workspace silently clipped its controls.");
                    backend.Resize(width, 20); app.RenderOnce();
                    if (Snapshot.ToText(app.CaptureFrame()!).Contains("Terminal too small", StringComparison.Ordinal) || !app.EnableMouseRouting) throw new InvalidDataException("Resizing did not restore the workspace and mouse routing.");
                    app.Stop();
                }
            }
        }
    }
}
#endif
