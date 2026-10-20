namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Layout;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class ConversationApp
    {
        private readonly ArgueBotClient _Client;
        private readonly ButtonRow _Actions = new ButtonRow();
        private readonly ListView<string> _Threads = new ListView<string>();
        private Pane _Transcript = new Pane("transcript");
        private readonly TextField _Composer = new TextField { Placeholder = "Add a note to this conversation (Enter sends). Start debate runs the agents." };
        private readonly Label _Feedback = new Label(Text.From("Connecting to the local service..."));
        private int _ActionInProgress;
        private DateTime _FeedbackUntil;
        private readonly StatusBar _Status = new StatusBar { RightText = "ArgueBot" };
        private List<Conversation> _Conversations = new List<Conversation>();
        private string? _Selected;
        private string _Search = "";
        private int _Page = 1;
        private bool _Archived;
        private string _LastContent = "";
        private TuiApplication? _App;
        private UiDispatcher? _Dispatcher;
        private CancellationToken _Token;
        internal ConversationApp(ArgueBotClient client) { _Client = client; }
        internal async Task RunAsync(CancellationToken token)
        {
            using (CancellationTokenSource polling = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                _Token = polling.Token;
                Task poll = Task.CompletedTask;
                try
                {
                    await TuiApp.RunAsync(app => { Configure(app); poll = PollAsync(polling.Token); app.Post(() => Launch(() => SplashScreen.ShowAsync(app, polling.Token))); }, token).ConfigureAwait(false);
                }
                finally { polling.Cancel(); try { await poll.ConfigureAwait(false); } catch (OperationCanceledException) { } }
            }
        }
        internal void Configure(TuiApplication app)
        {
            _App = app; _Dispatcher = new UiDispatcher(app, _Token); app.ApplyThemeToWidgets = true; app.Theme = TerminalTheme.Dark; app.HighlightFocusedRegion = true;
            _Actions.Add("Help", () => Launch(HelpAsync)).TooltipText = "Open help topics and keyboard instructions (F1).";
            _Actions.Add("Menu", () => Launch(MenuAsync)).TooltipText = "Browse every action without memorizing shortcuts (F2).";
            _Actions.Add("New", () => Launch(NewAsync)).TooltipText = "Create a debate with guided setup, or create an empty conversation.";
            _Actions.Add("Agents", () => Launch(ParticipantsAsync)).TooltipText = "Add or edit the CLI harnesses and REST models used as debate speakers.";
            _Actions.Add("Start debate", () => Launch(StartAsync)).TooltipText = "Choose speakers and settings for the selected conversation; review before starting.";
            _Actions.Add("Run controls", () => Launch(ControlsAsync)).TooltipText = "Pause, resume, interrupt, extend or stop a debate.";
            app.AddWidget("actions", _Actions, region => region.TopAnchored(0, 4).FillWidth().WithBorder(BorderStyle.Line, "Actions: click, or Tab then Enter").WithPadding(1, 0, 1, 0));
            app.AddWidget("threads", _Threads, region => region.LeftAnchored(0, 28).FillHeight(4, 5).WithBorder(BorderStyle.Line, "Conversations").WithPadding(1, 0, 1, 0));
            _Transcript = app.AddPane("transcript", region => region.FillWidth(28, 0).FillHeight(4, 5).WithBorder(BorderStyle.Line, "Shared debate history").WithPadding(1, 0, 1, 0));
            app.AddWidget("composer", _Composer, region => region.BottomAnchored(2, 3).FillWidth().WithBorder(BorderStyle.Line, "Note: Enter sends").WithPadding(1, 0, 1, 0));
            app.AddWidget("feedback", _Feedback, region => region.BottomAnchored(1, 1).FillWidth().WithPadding(1, 0, 1, 0));
            app.AddWidget("status", _Status, region => region.BottomAnchored(0, 1).FillWidth().WithPadding(1, 0, 1, 0));
            _Feedback.Content = Text.From("Connecting to the local service...", app.Theme.Muted);
            TranscriptFormatter.Render(_Transcript, Array.Empty<Message>(), app.Theme);
            _Composer.ApplyTheme(TerminalTheme.Inputs(app.Theme));
            app.BindKeyHints(_Status).AddAppHint("f1", "Help").AddAppHint("f2", "Menu").AddAppHint("ctrl+n", "New conversation").AddAppHint("ctrl+b", "Start debate").AddAppHint("ctrl+p", "Pause/resume").AddAppHint("ctrl+r", "Run controls").AddAppHint("ctrl+f", "Search").AddAppHint("ctrl+a", "Follow-up").AddAppHint("ctrl+e", "Participants").AddAppHint("ctrl+k", "Recovery").AddAppHint("ctrl+j", "Security audit").AddAppHint("ctrl+u", "Users").AddAppHint("ctrl+o", "Roles").AddAppHint("ctrl+g", "Attempts").AddAppHint("ctrl+l", "History").AddAppHint("ctrl+t", "Theme").AddAppHint("ctrl+q", "Quit");
            app.Bind("f2", () => Launch(MenuAsync));
            app.Bind("ctrl+q", app.Quit);
            app.Bind("ctrl+n", () => Launch(NewAsync));
            app.Bind("ctrl+b", () => Launch(StartAsync));
            app.Bind("ctrl+p", () => Launch(PauseAsync));
            app.Bind("ctrl+r", () => Launch(ControlsAsync));
            app.Bind("ctrl+a", () => Launch(FollowupAsync));
            app.Bind("ctrl+f", () => Launch(SearchAsync));
            app.Bind("ctrl+e", () => Launch(ParticipantsAsync));
            app.Bind("ctrl+k", () => Launch(RecoveryAsync));
            app.Bind("ctrl+j", () => Launch(AuditAsync));
            app.Bind("ctrl+u", () => Launch(UsersAsync));
            app.Bind("ctrl+o", () => Launch(RolesAsync));
            app.Bind("ctrl+g", () => Launch(AttemptsAsync));
            app.Bind("ctrl+l", () => Launch(HistoryAsync));
            app.Bind("alt+right", () => _Page++);
            app.Bind("alt+left", () => _Page = Math.Max(1, _Page - 1));
            app.Bind("ctrl+t", () => { app.Theme = app.Theme.Name == "Dark" ? Theme.Light : TerminalTheme.Dark; _Composer.ApplyTheme(TerminalTheme.Inputs(app.Theme)); _LastContent = ""; });
            app.Bind("f1", () => Launch(HelpAsync));
            _Threads.ItemActivated += index => { if (index >= 0 && index < _Conversations.Count) { _Selected = _Conversations[index].Id; _LastContent = ""; } };
            _Composer.Submitted += (_, _) => Launch(SendAsync);
        }
        private async Task PollAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Page page = await _Client.ConversationsAsync(_Search, _Archived, _Page, token).ConfigureAwait(false);
                    if (_Selected == null && page.Items.Count > 0) _Selected = page.Items[0].Id;
                    string? selected = _Selected;
                    MessagePage history = selected == null ? new MessagePage() : await _Client.MessagePageAsync(selected, tail: true, token: token).ConfigureAwait(false);
                    List<Message> messages = history.Items;
                    string content = (selected ?? "welcome") + "\n" + string.Join("\n\n", messages.Select(message => "[" + message.Speaker + " | " + message.Kind + " | " + message.State + "]\n" + message.Content + (message.ContentTruncated ? "\n[Preview: Ctrl+L opens full message history]" : "")));
                    _App?.Post(() =>
                    {
                        _Conversations = page.Items; int index = _Threads.SelectedIndex;
                        _Threads.SetItems(page.Items.Select(item => (item.Id == selected ? "> " : "") + (item.Pinned ? "* " : "") + item.Title));
                        if (index >= 0 && index < page.Items.Count) _Threads.Select(index);
                        if (_LastContent != content) { TranscriptFormatter.Render(_Transcript, messages, _App!.Theme, page.Items.FirstOrDefault(item => item.Id == selected)?.Title); _LastContent = content; }
                        if (DateTime.UtcNow >= _FeedbackUntil) _Feedback.Content = Text.From("Connected | " + page.TotalCount + " conversations | Page " + _Page + " | " + history.TotalCount + " messages", _App!.Theme.Muted);
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException) { _App?.Post(() => _Feedback.Content = Text.From("Connection: " + ex.Message, _App!.Theme.Error)); }
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
        private void Launch(Func<Task> action) { _ = ExecuteActionAsync(action); }
        private async Task ExecuteActionAsync(Func<Task> action)
        {
            if (Interlocked.CompareExchange(ref _ActionInProgress, 1, 0) != 0) return;
            try { await action().ConfigureAwait(false); }
            catch (OperationCanceledException) when (_Token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _App?.Post(() => _Feedback.Content = Text.From(ex.Message, _App!.Theme.Error));
                if (_App != null && !_Token.IsCancellationRequested)
                {
                    try { await UiAsync(() => _App.ConfirmStyledAsync("Operation failed: " + ex.Message, "Close", "Close")).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_Token.IsCancellationRequested) { }
                }
            }
            finally { Volatile.Write(ref _ActionInProgress, 0); }
        }
        private async Task HistoryAsync()
        {
            if (_App == null || _Selected == null) return;
            await new MessageHistoryWorkflow(_Client, _App, _Dispatcher ?? throw new InvalidOperationException("Terminal dispatcher is unavailable."), _Token).RunAsync(_Selected).ConfigureAwait(false);
        }
        private async Task AttemptsAsync()
        {
            if (_App == null || _Selected == null) return;
            await new AttemptWorkflow(_Client, _App, _Dispatcher ?? throw new InvalidOperationException("Terminal dispatcher is unavailable."), _Token).RunAsync(_Selected).ConfigureAwait(false);
        }
        private async Task RolesAsync()
        {
            if (_App == null) return;
            await new RoleWorkflow(_Client, _App, _Dispatcher ?? throw new InvalidOperationException("Terminal dispatcher is unavailable."), _Token).RunAsync().ConfigureAwait(false);
        }
        private async Task UsersAsync()
        {
            if (_App == null) return;
            await new UserWorkflow(_Client, _App, _Dispatcher ?? throw new InvalidOperationException("Terminal dispatcher is unavailable."), _Token).RunAsync().ConfigureAwait(false);
        }
        private async Task AuditAsync()
        {
            if (_App == null) return;
            await new AuditWorkflow(_Client, _App, _Dispatcher ?? throw new InvalidOperationException("Terminal dispatcher is unavailable."), _Token).RunAsync().ConfigureAwait(false);
        }
        private async Task RecoveryAsync()
        {
            if (_App == null) return;
            RecoveryWorkflow workflow = new RecoveryWorkflow(_Client, _App, _Dispatcher ?? throw new InvalidOperationException("Terminal dispatcher is unavailable."), _Token);
            await workflow.RunAsync().ConfigureAwait(false);
        }
        private async Task ParticipantsAsync()
        {
            if (_App == null) return;
            List<RunnerProfile> profiles = await _Client.ParticipantsAsync(_Token).ConfigureAwait(false);
            string[] choices = new[] { "Add agent: CLI harness or REST model" }.Concat(profiles.Select(profile => profile.Name + " [" + profile.Kind + "/" + profile.Provider + "]" + (profile.Enabled ? "" : " disabled"))).ToArray();
            int selected = await UiAsync(() => _App.SelectStyledAsync("Agents: choose Add agent, or select an existing agent", choices)).ConfigureAwait(false);
            if (selected < 0) return;
            RunnerProfile? profile = selected == 0 ? null : profiles[selected - 1];
            if (profile != null)
            {
                int action = await UiAsync(() => _App.SelectStyledAsync(profile.Name, "Edit agent", "Delete reusable profile (history remains)")).ConfigureAwait(false);
                if (action < 0) return;
                if (action == 1)
                {
                    if (await UiAsync(() => _App.ConfirmStyledAsync("Delete " + profile.Name + "? Historical run snapshots remain readable.")).ConfigureAwait(false))
                        await _Client.SendAsync<DeletionResponse>(HttpMethod.Delete, "v1.0/api/participants/" + Uri.EscapeDataString(profile.Id), token: _Token).ConfigureAwait(false);
                    return;
                }
            }
            RunnerProfile? preset = null;
            if (profile == null)
            {
                int kind = await UiAsync(() => _App.SelectStyledAsync("Add agent: choose how it runs", AgentPresets.Choices)).ConfigureAwait(false);
                if (kind < 0) return;
                preset = AgentPresets.Create(kind);
            }
            RunnerProfile? updated = await UiAsync(async () =>
            {
                ParticipantEditor editor = new ParticipantEditor(profile ?? preset);
                FormDialog<RunnerProfile> dialog = new FormDialog<RunnerProfile>(profile == null ? "Add agent" : "Edit agent", editor.Form, editor.Extract, _App, async candidate =>
                {
                    if (profile == null) await _Client.CreateParticipantAsync(candidate, _Token).ConfigureAwait(false);
                    else await _Client.SendAsync<RunnerProfile>(HttpMethod.Put, "v1.0/api/participants/" + Uri.EscapeDataString(profile.Id), candidate, _Token).ConfigureAwait(false);
                });
                dialog.ApplyTheme(_App.Theme);
                return await _App.ShowAsync<RunnerProfile>(dialog).ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (updated == null) return;
            _App.Post(() => { _FeedbackUntil = DateTime.UtcNow.AddSeconds(5); _Feedback.Content = Text.From("Agent saved. Choose New or Start debate to use it.", _App.Theme.Success); });
        }
        private async Task NewAsync()
        {
            if (_App == null) return;
            while (true)
            {
                int action = await UiAsync(() => _App.SelectStyledAsync("New: what would you like to do?", "Start a debate: guided setup", "Create an empty conversation (start a debate later)", "Set up agents first (CLI harnesses / REST models)")).ConfigureAwait(false);
                if (action < 0) return;
                if (action == 2) { await ParticipantsAsync().ConfigureAwait(false); continue; }
                if (action == 0) { await ConfigureDebateAsync(null).ConfigureAwait(false); return; }
                string? title = await UiAsync(() => _App.PromptStyledAsync("What question is this conversation about?")).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(title)) return;
                Conversation conversation = await _Client.CreateConversationAsync(title, _Token).ConfigureAwait(false); _Selected = conversation.Id; _LastContent = "";
                return;
            }
        }
        private async Task SendAsync()
        {
            if (_Selected == null) throw new InvalidOperationException("Create or open a conversation first.");
            string text = _Composer.Value; if (string.IsNullOrWhiteSpace(text)) return;
            await _Client.NoteAsync(_Selected, text, token: _Token).ConfigureAwait(false);
            _App?.Post(() => _Composer.Value = "");
        }
        private async Task StartAsync()
        {
            await ConfigureDebateAsync(_Selected).ConfigureAwait(false);
        }
        private async Task ConfigureDebateAsync(string? conversationId)
        {
            if (_App == null) return;
            List<RunnerProfile> profiles = await _Client.ParticipantsAsync(_Token).ConfigureAwait(false);
            while (profiles.Count(profile => profile.Enabled) < 2)
            {
                int action = await UiAsync(() => _App.SelectStyledAsync("Set up agents: " + profiles.Count(profile => profile.Enabled) + " of 2 enabled", "Add or enable an agent (CLI harness or REST model)", "Cancel debate setup")).ConfigureAwait(false);
                if (action != 0) return;
                await ParticipantsAsync().ConfigureAwait(false);
                profiles = await _Client.ParticipantsAsync(_Token).ConfigureAwait(false);
            }
            string question = conversationId == null ? "" : (await _Client.ConversationAsync(conversationId, _Token).ConfigureAwait(false)).Title;
            CreateRunRequest? request = await UiAsync(() =>
            {
                DebateSetupEditor editor = new DebateSetupEditor(profiles, question, conversationId != null);
                return _App.ShowAsync<CreateRunRequest>(new DebateWizardDialog(editor));
            }).ConfigureAwait(false);
            if (request == null) return;
            if (conversationId == null)
            {
                Conversation conversation = await _Client.CreateConversationAsync(request.Question, _Token).ConfigureAwait(false);
                conversationId = conversation.Id; _Selected = conversationId; _LastContent = "";
            }
            await _Client.StartAsync(conversationId, request, _Token).ConfigureAwait(false);
        }

        private async Task PauseAsync()
        {
            if (_Selected == null) return;
            List<DebateRun> runs = await _Client.RunsAsync(_Selected, _Token).ConfigureAwait(false);
            DebateRun? run = runs.LastOrDefault(); if (run == null) return;
            await _Client.ActionAsync(_Selected, run.Id, new CommandRequest { Action = run.State == "paused" ? "resume" : "pause", Revision = run.Revision }, _Token).ConfigureAwait(false);
        }
        private Task<T> UiAsync<T>(Func<Task<T>> action)
        {
            return (_Dispatcher ?? throw new InvalidOperationException("Terminal is not running.")).InvokeAsync(action);
        }
        private async Task SearchAsync()
        {
            if (_App == null) return;
            string? search = await UiAsync(() => _App.PromptStyledAsync("Search conversation titles and message content", _Search)).ConfigureAwait(false);
            if (search == null) return;
            int archive = await UiAsync(() => _App.SelectStyledAsync("Conversation scope", "Active conversations", "Archived conversations")).ConfigureAwait(false);
            if (archive < 0) return;
            _Search = search; _Archived = archive == 1; _Page = 1;
        }
        private async Task FollowupAsync()
        {
            if (_App == null || _Selected == null) return;
            string conversationId = _Selected;
            List<RunnerProfile> profiles = (await _Client.ParticipantsAsync(_Token).ConfigureAwait(false)).Where(profile => profile.Enabled).ToList();
            if (profiles.Count == 0) throw new InvalidOperationException("Configure an enabled participant first.");
            int index = await UiAsync(() => _App.SelectStyledAsync("Choose follow-up respondent", profiles.Select(profile => profile.Name).ToArray())).ConfigureAwait(false);
            if (index < 0) return;
            string? question = await UiAsync(() => _App.PromptStyledAsync("Follow-up question")).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(question)) return;
            await _Client.StartAsync(conversationId, new CreateRunRequest { Mode = "followup", Question = question, ParticipantIds = new List<string> { profiles[index].Id }, IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
        }
        private async Task ControlsAsync()
        {
            if (_App == null) return;
            if (_Selected == null) { await NewAsync().ConfigureAwait(false); return; }
            string conversationId = _Selected;
            DebateRun? run = (await _Client.RunsAsync(conversationId, _Token).ConfigureAwait(false)).LastOrDefault();
            if (run == null)
            {
                int choice = await UiAsync(() => _App.SelectStyledAsync("This conversation has no debate yet", "Set up and start a debate", "Back to conversation")).ConfigureAwait(false);
                if (choice == 0) await ConfigureDebateAsync(conversationId).ConfigureAwait(false);
                return;
            }
            string[] actions = new string[] { "pause", "resume", "interrupt", "continue", "extend", "steer", "stop" };
            int selected = await UiAsync(() => _App.SelectStyledAsync("Run " + run.State + " | " + run.Outcome + ": choose a command", "Pause after turn", "Resume interrupted run (retry may incur cost)", "Interrupt current generation", "Continue immediately", "Extend by two rounds", "Private directive to a participant", "Stop run")).ConfigureAwait(false);
            if (selected < 0) return;
            CommandRequest command = new CommandRequest { Action = actions[selected], Revision = run.Revision, IdempotencyKey = Guid.NewGuid().ToString("N") };
            if (command.Action == "steer")
            {
                int recipient = await UiAsync(() => _App.SelectStyledAsync("Directive recipient (only this participant receives the text)", run.Participants.Select(profile => profile.Name).ToArray())).ConfigureAwait(false);
                if (recipient < 0) return;
                string? text = await UiAsync(() => _App.PromptStyledAsync("Private directive for " + run.Participants[recipient].Name)).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text)) return;
                command.RecipientId = run.Participants[recipient].Id; command.Text = text;
            }
            if (command.Action == "stop" || command.Action == "interrupt")
            {
                bool accepted = await UiAsync(() => _App.ConfirmStyledAsync(command.Action == "stop" ? "Stop this run? Partial answers remain in history." : "Interrupt the current answer? Partial text remains; resumption retries the turn.")).ConfigureAwait(false);
                if (!accepted) return;
            }
            await _Client.ActionAsync(conversationId, run.Id, command, _Token).ConfigureAwait(false);
        }
        private async Task MenuAsync()
        {
            if (_App == null) return;
            List<string> labels = new List<string>(); List<Func<Task>> actions = new List<Func<Task>>();
            void Add(string label, Func<Task> action) { labels.Add(label); actions.Add(action); }
            Add("New debate or conversation", NewAsync);
            Add("Agents: configure CLI harnesses and REST models", ParticipantsAsync);
            if (_Selected != null)
            {
                Add("Start a debate in this conversation", StartAsync);
                Add("Run controls: pause, resume, interrupt or stop", ControlsAsync);
                Add("Read full message history", HistoryAsync);
                Add("Inspect generation attempts", AttemptsAsync);
                Add("Ask an agent a follow-up question", FollowupAsync);
            }
            Add("Search conversations and archived history", SearchAsync);
            Add("Administration and security audit", AdministrationAsync);
            Add("Switch light / dark theme", () => { _App.Post(() => { _App.Theme = _App.Theme.Name == "Dark" ? Theme.Light : TerminalTheme.Dark; _Composer.ApplyTheme(TerminalTheme.Inputs(_App.Theme)); _LastContent = ""; }); return Task.CompletedTask; });
            Add("Help: how to use ArgueBot", HelpAsync);
            Add("Detach (the service keeps running)", () => { _App.Post(_App.Quit); return Task.CompletedTask; });
            int selected = await UiAsync(() => _App.SelectStyledAsync("Main menu: choose an action", labels.ToArray())).ConfigureAwait(false);
            if (selected >= 0) await actions[selected]().ConfigureAwait(false);
        }
        private async Task AdministrationAsync()
        {
            if (_App == null) return;
            int selected = await UiAsync(() => _App.SelectStyledAsync("Administration: service permissions apply", "Users", "Roles", "Security audit", "Backup and recovery")).ConfigureAwait(false);
            if (selected == 0) await UsersAsync().ConfigureAwait(false);
            else if (selected == 1) await RolesAsync().ConfigureAwait(false);
            else if (selected == 2) await AuditAsync().ConfigureAwait(false);
            else if (selected == 3) await RecoveryAsync().ConfigureAwait(false);
        }
        private async Task HelpAsync()
        {
            if (_App == null) return;
            string[] topics = new[] { "Navigation and conversations", "Configure agents and harnesses", "Start and control a debate", "History, messages and attempts", "Users, roles, audit and recovery", "Theme and detaching" };
            string[][] shortcuts = new[]
            {
                new[] { "Use the visible buttons: Help, Menu, New, Agents, Start debate and Run controls", "Tab / Shift+Tab: move focus; arrows move within a list or button row", "Enter: open a conversation or send the composer note", "New / Ctrl+N: choose a guided debate or an empty conversation", "Menu / F2: browse all available actions", "Ctrl+F: search titles/content and choose archive scope", "Alt+Left / Alt+Right: change conversation pages" },
                new[] { "Ctrl+E: create, edit or delete reusable participant profiles", "CLI: choose protocol, executable, working directory and literal arguments", "REST: choose provider, endpoint, model and credential environment reference", "Configure at least two enabled participants for a debate", "Profiles execute on the service machine; prompts go to CLI stdin", "Tab: next form field | Ctrl+S: save | Esc: cancel" },
                new[] { "Start debate / Ctrl+B: Question, Speakers, Settings, then Review", "Back / Next: move between steps without losing answers", "Start debate on the Review step: submit the debate", "Ctrl+P: pause or resume", "Ctrl+R: run controls, interrupt, extend, private directive or stop", "Ctrl+A: ask a follow-up question", "Interrupted turns require an explicit resume; a retry may incur cost" },
                new[] { "Ctrl+L: browse history and read complete messages", "Ctrl+G: inspect generation attempts and exact source context", "Preview notices mean more text is available through Ctrl+L", "PageUp / PageDown: scroll inside full-message readers", "Private directives are sent only to the selected participant" },
                new[] { "Ctrl+U: manage tenant users (administrator)", "Ctrl+O: browse, clone and edit role definitions (administrator)", "Ctrl+J: inspect security audit events", "Ctrl+K: backup/recovery and durable jobs (administrator)", "Access is checked by the service; protected records cannot be edited" },
                new[] { "Ctrl+T: switch dark/light theme", "Dark: black workspace and dark grey input fields", "Ctrl+Q: detach the terminal without stopping service-owned work", "F1: reopen this help menu", "Esc: close a dialog and return" }
            };
            while (true)
            {
                int selected = await UiAsync(() => _App.SelectStyledAsync("Help", topics)).ConfigureAwait(false);
                if (selected < 0) return;
                await UiAsync(() => _App.ShowAsync(new MessageDialog(new Message { Content = string.Join("\n\n", shortcuts[selected]), Speaker = "Help" }) { Title = "Help: " + topics[selected] })).ConfigureAwait(false);
            }
        }
    }
}
