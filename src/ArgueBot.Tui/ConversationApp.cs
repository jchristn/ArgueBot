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
    using ArgueBot.Core.Services;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Layout;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    internal sealed class ConversationApp
    {
        private readonly ArgueBotClient _Client;
        private readonly ButtonRow _Actions = new ButtonRow();
        private readonly ConversationListView _Threads = new ConversationListView();
        private readonly Pane _Transcript = new Pane("transcript");
        private readonly TextField _Composer = new TextField { Placeholder = "Add a note to this conversation (Enter sends). Start debate runs the agents." };
        private readonly Label _ConversationHeading = new Label(Text.From("Conversations"));
        private readonly Label _NoteHeading = new Label(Text.From("Note: Enter sends"));
        private readonly Label _HistoryTitle = new Label(Text.From("Shared debate history"));
        private readonly Label _Feedback = new Label(Text.From("Connecting to the local service..."));
        private int _ActionInProgress;
        private DateTime _FeedbackUntil;
        private readonly StatusBar _Status = new StatusBar { RightText = "ArgueBot", ReservedHint = KeyHint.For("f1", "Help") };
        private List<Conversation> _Conversations = new List<Conversation>();
        private string? _Selected;
        private string? _SelectedTitle;
        private long _ViewRevision;
        private int _TotalPages = 1;
        private readonly Dictionary<string, string> _Drafts = new Dictionary<string, string>();
        private string _Search = "";
        private int _Page = 1;
        private bool _Archived;
        private string _LastContent = "";
        private TuiApplication? _App;
        private UiDispatcher? _Dispatcher;
        private CancellationToken _Token;
        internal bool IsBusy { get { return Volatile.Read(ref _ActionInProgress) != 0; } }
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
            _App = app; _Dispatcher = new UiDispatcher(app, _Token); app.ApplyThemeToWidgets = true; app.Theme = TerminalTheme.Dark; app.HighlightFocusedRegion = false;
            _ = new WorkspaceViewport(app, () => _SelectedTitle ?? "Shared debate history");
            _Actions.Add("Help", () => Launch(HelpAsync)).TooltipText = "Open help topics and keyboard instructions (F1).";
            _Actions.Add("Menu", () => Launch(MenuAsync)).TooltipText = "Browse every action without memorizing shortcuts (F2).";
            _Actions.Add("New", () => Launch(NewAsync)).TooltipText = "Create a debate with guided setup, or create an empty conversation.";
            _Actions.Add("Agents", () => Launch(ParticipantsAsync)).TooltipText = "Add or edit the CLI harnesses and REST models used as debate speakers.";
            _Actions.Add("Start debate", () => Launch(StartAsync)).TooltipText = "Choose speakers and settings for the selected conversation; review before starting.";
            _Actions.Add("Run controls", () => Launch(ControlsAsync)).TooltipText = "Pause, resume, interrupt, extend or stop a debate.";
            app.AddWidget("actions", _Actions, region => region.TopAnchored(0, 4).FillWidth().WithPadding(1, 1, 1, 0));
            app.AddWidget("conversation-heading", _ConversationHeading, region => region.ProportionalWidth(0, 0.3).TopAnchored(4, 1).WithPadding(1, 0, 2, 0));
            app.AddWidget("history-heading", _HistoryTitle, region => region.ProportionalWidth(0.3, 0.7).TopAnchored(4, 1).WithPadding(1, 0, 1, 0));
            app.AddWidget("note-heading", _NoteHeading, region => region.BottomAnchored(4, 1).FillWidth().WithPadding(1, 0, 1, 0));
            app.AddWidget("threads", _Threads, region => region.ProportionalWidth(0, 0.3).FillHeight(6, 5).WithPadding(1, 0, 2, 0));
            TranscriptView history = new TranscriptView(_Transcript);
            history.ReadRequested += () => Launch(HistoryAsync);
            app.AddWidget("transcript", history, region => region.ProportionalWidth(0.3, 0.7).FillHeight(6, 5).WithPadding(1, 0, 1, 0));
            app.AddWidget("composer", _Composer, region => region.BottomAnchored(2, 2).FillWidth().WithPadding(1, 0, 1, 0));
            app.AddWidget("feedback", _Feedback, region => region.BottomAnchored(1, 1).FillWidth().WithPadding(1, 0, 1, 0));
            app.AddWidget("status", _Status, region => region.BottomAnchored(0, 1).FillWidth().WithPadding(1, 0, 1, 0));
            _Feedback.Content = Text.From("Connecting to the local service...", app.Theme.Muted);
            TranscriptFormatter.Render(_Transcript, Array.Empty<Message>(), app.Theme);
            ApplyWorkspaceTheme();
            app.BindKeyHints(_Status).AddAppHint("f1", "Help").AddAppHint("f2", "Menu").AddAppHint("ctrl+q", "Detach");
            app.Bind("f2", () => Launch(MenuAsync));
            app.Bind("alt+1", () => app.Focus("threads"));
            app.Bind("alt+2", () => app.Focus("transcript"));
            app.Bind("alt+3", () => app.Focus("composer"));
            app.Bind("escape", () => app.Focus("threads"));
            app.Bind("ctrl+q", app.Quit);
            app.Bind("ctrl+n", () => Launch(NewAsync));
            app.Bind("ctrl+b", () => Launch(StartAsync));
            app.Bind("ctrl+p", () => Launch(PauseAsync));
            app.Bind("f8", () => Launch(StopAsync));
            app.Bind("ctrl+r", () => Launch(ControlsAsync));
            app.Bind("ctrl+y", () => Launch(FollowupAsync));
            app.Bind("ctrl+f", () => Launch(SearchAsync));
            app.Bind("ctrl+e", () => Launch(ParticipantsAsync));
            app.Bind("ctrl+g", () => Launch(AttemptsAsync));
            app.Bind("ctrl+l", () => Launch(HistoryAsync));
            app.Bind("alt+right", () => ChangePage(1));
            app.Bind("alt+left", () => ChangePage(-1));
            app.Bind("ctrl+t", ToggleTheme);
            app.Bind("f1", () => Launch(HelpAsync));
            _Threads.ItemActivated += index => { if (index >= 0 && index < _Conversations.Count) { OpenConversation(_Conversations[index].Id, _Conversations[index].Title); } };
            _Threads.CommandRequested += ConversationCommand;
            _Composer.Submitted += (_, _) => Launch(SendAsync);
            app.Focus("threads");
        }
        private void ConversationCommand(string command)
        {
            if (IsBusy) return;
            if (command == "n") { Launch(NewAsync); return; }
            if (command == "/") { Launch(SearchAsync); return; }
            if (command == "e") { Launch(ParticipantsAsync); return; }
            if (command == "?") { Launch(HelpAsync); return; }
            int index = _Threads.SelectedIndex;
            if (index < 0 || index >= _Conversations.Count) return;
            Conversation target = _Conversations[index];
            if (_Selected != target.Id) OpenConversation(target.Id, target.Title);
            switch (command)
            {
                case "d": Launch(DeleteConversationAsync); break;
                case "b": Launch(StartAsync); break;
                case "p": Launch(PauseAsync); break;
                case "s": Launch(StopAsync); break;
                case "r": Launch(ControlsAsync); break;
                case "f": Launch(FollowupAsync); break;
                case "l": Launch(HistoryAsync); break;
                case "g": Launch(AttemptsAsync); break;
                case "a": _App!.Focus("composer"); break;
            }
        }
        private void ApplyWorkspaceTheme()
        {
            if (_App == null) return;
            _Composer.ApplyTheme(TerminalTheme.Inputs(_App.Theme));
            _ConversationHeading.Content = Text.From("Conversations", _App.Theme.Muted);
            _NoteHeading.Content = Text.From("Note: Enter sends", _App.Theme.Muted);
            _HistoryTitle.Content = Text.From(_SelectedTitle ?? "Shared debate history", _App.Theme.Text).Bold();
            _LastContent = "";
        }
        private void ToggleTheme()
        {
            if (_App == null) return;
            _App.Theme = _App.Theme.Name == "Dark" ? Theme.Light : TerminalTheme.Dark;
            ApplyWorkspaceTheme();
        }
        private void OpenConversation(string id, string title)
        {
            if (_Selected != null) _Drafts[_Selected] = _Composer.Value;
            _Selected = id; _SelectedTitle = title; _HistoryTitle.Content = Text.From(title, _App!.Theme.Text).Bold(); _LastContent = ""; _ViewRevision++;
            _Composer.Value = _Drafts.TryGetValue(id, out string? draft) ? draft : "";
            _Transcript.Clear(); _Transcript.WriteLine("Opening " + title + "...");
        }
        private void ChangePage(int delta)
        {
            int page = (int)Math.Max(1, Math.Min(_TotalPages, (long)_Page + delta));
            if (page != _Page) { _Page = page; _ViewRevision++; }
        }
        private async Task PollAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { await RefreshAsync(token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
                { _App?.Post(() => _Feedback.Content = Text.From("Connection: " + ex.Message, _App!.Theme.Error)); }
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
        internal async Task RefreshAsync(CancellationToken token)
        {
            ConversationQuery query = await UiAsync(() => Task.FromResult(new ConversationQuery { Revision = _ViewRevision, Search = _Search, Archived = _Archived, PageNumber = _Page, SelectedId = _Selected, SelectedTitle = _SelectedTitle })).ConfigureAwait(false);
            try
            {
                Page page = await _Client.ConversationsAsync(query.Search, query.Archived, query.PageNumber, token).ConfigureAwait(false);
                string? selected = query.SelectedId ?? page.Items.FirstOrDefault()?.Id;
                string? title = page.Items.FirstOrDefault(item => item.Id == selected)?.Title ?? query.SelectedTitle;
                MessagePage history = selected == null ? new MessagePage() : await _Client.MessagePageAsync(selected, tail: true, token: token).ConfigureAwait(false);
                string content = (selected ?? "welcome") + "\n" + title + "\n" + string.Join("\n\n", history.Items.Select(message => message.Speaker + " | " + message.Kind + " | " + message.State + "\n" + message.Content + "\n" + message.ContentTruncated));
                await UiAsync(() =>
                {
                    if (_ViewRevision != query.Revision) return Task.FromResult(false);
                    string? cursor = _Threads.SelectedIndex >= 0 && _Threads.SelectedIndex < _Conversations.Count ? _Conversations[_Threads.SelectedIndex].Id : null;
                    if (_Selected == null && selected != null) OpenConversation(selected, title ?? "Conversation");
                    _SelectedTitle = title; _HistoryTitle.Content = Text.From(title ?? "Shared debate history", _App!.Theme.Text).Bold(); _Conversations = page.Items;
                    _Threads.SetItems(page.Items.Select(item => (item.Id == selected ? "> " : "") + (item.Pinned ? "* " : "") + item.Title));
                    int index = page.Items.FindIndex(item => item.Id == cursor);
                    if (index < 0) index = page.Items.FindIndex(item => item.Id == selected);
                    if (index >= 0) _Threads.Select(index);
                    _TotalPages = Math.Max(1, (int)Math.Min(int.MaxValue, (page.TotalCount + page.PageSize - 1) / Math.Max(1, page.PageSize)));
                    if (_LastContent != content) { TranscriptFormatter.Render(_Transcript, history.Items, _App!.Theme, title); _LastContent = content; }
                    if (DateTime.UtcNow >= _FeedbackUntil) _Feedback.Content = Text.From("Connected | " + page.TotalCount + " conversations | Page " + _Page + "/" + _TotalPages + " | " + history.TotalCount + " messages", _App!.Theme.Muted);
                    return Task.FromResult(true);
                }).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException)
            {
                bool stale = await UiAsync(() => Task.FromResult(_ViewRevision != query.Revision)).ConfigureAwait(false);
                if (!stale) throw;
            }
        }
        private void Launch(Func<Task> action) { _ = ExecuteActionAsync(action); }
        private async Task ExecuteActionAsync(Func<Task> action)
        {
            if (Interlocked.CompareExchange(ref _ActionInProgress, 1, 0) != 0) return;
            _App?.Post(() => { foreach (Button button in _Actions.Buttons) button.IsEnabled = false; });
            try { await action().ConfigureAwait(false); }
            catch (OperationCanceledException) when (_Token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                string message = ex is DomainException domain && domain.Code == "active_run"
                    ? ex.Message + " Close this message, highlight the conversation and press s to stop its debate, then d to delete. F8 stops the open conversation."
                    : ex.Message;
                _App?.Post(() => _Feedback.Content = Text.From(message, _App!.Theme.Error));
                if (_App != null && !_Token.IsCancellationRequested)
                {
                    try { await UiAsync(() => _App.ConfirmStyledAsync("Operation failed: " + message, "Close", "Close")).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (_Token.IsCancellationRequested) { }
                }
            }
            finally { Volatile.Write(ref _ActionInProgress, 0); _App?.Post(() => { foreach (Button button in _Actions.Buttons) button.IsEnabled = true; }); }
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
                Conversation conversation = await _Client.CreateConversationAsync(title, _Token).ConfigureAwait(false);
                await UiAsync(() => { OpenConversation(conversation.Id, conversation.Title); return Task.FromResult(true); }).ConfigureAwait(false);
                return;
            }
        }
        private async Task SendAsync()
        {
            if (_Selected == null) throw new InvalidOperationException("Create or open a conversation first.");
            string text = _Composer.Value; if (string.IsNullOrWhiteSpace(text)) return;
            string conversationId = _Selected;
            await _Client.NoteAsync(conversationId, text, token: _Token).ConfigureAwait(false);
            _App?.Post(() => { if (_Selected == conversationId && _Composer.Value == text) _Composer.Value = ""; if (_Drafts.TryGetValue(conversationId, out string? draft) && draft == text) _Drafts.Remove(conversationId); });
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
                return _App.ShowAsync<CreateRunRequest>(new DebateWizardDialog(editor, _App, async candidate =>
                {
                    if (conversationId == null)
                    {
                        Conversation conversation = await _Client.CreateConversationAsync(candidate.Question, _Token).ConfigureAwait(false);
                        conversationId = conversation.Id;
                        await UiAsync(() => { OpenConversation(conversation.Id, conversation.Title); return Task.FromResult(true); }).ConfigureAwait(false);
                    }
                    await _Client.StartAsync(conversationId, candidate, _Token).ConfigureAwait(false);
                }));
            }).ConfigureAwait(false);
            if (request != null) _App.Post(() => { _FeedbackUntil = DateTime.UtcNow.AddSeconds(5); _Feedback.Content = Text.From("Debate started. Press s in the conversation list or F8 to stop it; r opens run controls.", _App.Theme.Success); });
        }

        private async Task StopAsync()
        {
            if (_App == null || _Selected == null) return;
            string conversationId = _Selected;
            string title = _SelectedTitle ?? conversationId;
            DebateRun? run = (await _Client.RunsAsync(conversationId, _Token).ConfigureAwait(false)).LastOrDefault(item => !RunFinished(item));
            if (run == null)
            {
                await UiAsync(() => { _FeedbackUntil = DateTime.UtcNow.AddSeconds(5); _Feedback.Content = Text.From("No unfinished debate. Press d in the conversation list to delete.", _App.Theme.Muted); return Task.FromResult(true); }).ConfigureAwait(false);
                return;
            }
            bool accepted = await UiAsync(() => _App.ConfirmStyledAsync("Stop debate in " + title + "? Current state: " + run.State + ". Partial answers remain in history. You can delete the conversation after it stops.", "Stop debate", "Cancel: keep debate")).ConfigureAwait(false);
            if (!accepted) return;
            // Stop targets this run regardless of revisions produced while the confirmation is open.
            await _Client.ActionAsync(conversationId, run.Id, new CommandRequest { Action = "stop", IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
            await UiAsync(() => { _FeedbackUntil = DateTime.UtcNow.AddSeconds(10); _Feedback.Content = Text.From("Stopping debate... waiting for the service.", _App.Theme.Muted); return Task.FromResult(true); }).ConfigureAwait(false);
            bool stopped = await WaitForStopAsync(conversationId, run.Id).ConfigureAwait(false);
            await UiAsync(() =>
            {
                _FeedbackUntil = DateTime.UtcNow.AddSeconds(10);
                _Feedback.Content = Text.From(stopped ? "Debate stopped. Press d in the conversation list to delete." : "Stop requested; the service is still stopping. Retry deletion after the debate stops.", stopped ? _App.Theme.Success : _App.Theme.Muted);
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        }
        private async Task<bool> WaitForStopAsync(string conversationId, string runId)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                DebateRun? current = (await _Client.RunsAsync(conversationId, _Token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == runId);
                if (current == null || RunFinished(current)) return true;
                await Task.Delay(100, _Token).ConfigureAwait(false);
            }
            return false;
        }
        private static bool RunFinished(DebateRun run) { return run.State == "completed" || run.State == "cancelled" || run.State == "failed"; }
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
            await UiAsync(() => { _Search = search; _Archived = archive == 1; _Page = 1; _ViewRevision++; return Task.FromResult(true); }).ConfigureAwait(false);
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
        private async Task DeleteConversationAsync()
        {
            if (_App == null || _Selected == null) return;
            string id = _Selected;
            Conversation conversation = await _Client.ConversationAsync(id, _Token).ConfigureAwait(false);
            int action = await UiAsync(() => _App.SelectStyledAsync("Permanently delete this conversation and its messages? This cannot be undone. Conversation: " + conversation.Title, "Cancel: keep this conversation", "Delete permanently")).ConfigureAwait(false);
            if (action != 1) return;
            while (true)
            {
                try
                {
                    await _Client.SendAsync<DeletionResponse>(HttpMethod.Delete, "v1.0/api/conversations/" + Uri.EscapeDataString(id), token: _Token).ConfigureAwait(false);
                    break;
                }
                catch (DomainException exception) when (exception.Code == "active_run")
                {
                    DebateRun? run = (await _Client.RunsAsync(id, _Token).ConfigureAwait(false)).LastOrDefault(item => !RunFinished(item));
                    if (run == null)
                    {
                        await _Client.SendAsync<DeletionResponse>(HttpMethod.Delete, "v1.0/api/conversations/" + Uri.EscapeDataString(id), token: _Token).ConfigureAwait(false);
                        break;
                    }
                    bool stopAndDelete = await UiAsync(() => _App.ConfirmStyledAsync("This conversation has an unfinished debate (" + run.State + "). Stop it and permanently delete the conversation and all its messages? This cannot be undone. Conversation: " + conversation.Title, "Stop debate and delete permanently", "Cancel: keep conversation and debate")).ConfigureAwait(false);
                    if (!stopAndDelete) return;
                    await _Client.ActionAsync(id, run.Id, new CommandRequest { Action = "stop", IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
                    await UiAsync(() => { _FeedbackUntil = DateTime.UtcNow.AddSeconds(10); _Feedback.Content = Text.From("Stopping debate before deletion...", _App.Theme.Muted); return Task.FromResult(true); }).ConfigureAwait(false);
                    while (!await WaitForStopAsync(id, run.Id).ConfigureAwait(false))
                    {
                        bool keepWaiting = await UiAsync(() => _App.ConfirmStyledAsync("The debate is still stopping. Keep waiting to delete " + conversation.Title + "? Cancelling keeps the conversation; the stop request remains in effect.", "Keep waiting, then delete", "Cancel deletion: keep conversation")).ConfigureAwait(false);
                        if (!keepWaiting)
                        {
                            await UiAsync(() => { _FeedbackUntil = DateTime.UtcNow.AddSeconds(10); _Feedback.Content = Text.From("Deletion cancelled. The conversation is kept; the stop request remains in effect.", _App.Theme.Muted); return Task.FromResult(true); }).ConfigureAwait(false);
                            return;
                        }
                    }
                }
            }
            await UiAsync(() =>
            {
                if (_Selected == id)
                {
                    _Selected = null; _SelectedTitle = null; _LastContent = ""; _Composer.Value = ""; _Drafts.Remove(id); _Page = 1; _ViewRevision++;
                    _Conversations.RemoveAll(item => item.Id == id);
                    _Threads.SetItems(_Conversations.Select(item => item.Title));
                    TranscriptFormatter.Render(_Transcript, Array.Empty<Message>(), _App.Theme);
                }
                _FeedbackUntil = DateTime.UtcNow.AddSeconds(5);
                _Feedback.Content = Text.From("Conversation deleted.", _App.Theme.Success);
                return Task.FromResult(true);
            }).ConfigureAwait(false);
        }
        private async Task MenuAsync()
        {
            if (_App == null) return;
            List<string> labels = new List<string>(); List<Func<Task>> actions = new List<Func<Task>>();
            void Add(string label, Func<Task> action) { labels.Add(label); actions.Add(action); }
            Add("New debate or conversation (Ctrl+N)", NewAsync);
            Add("Agents: configure CLI harnesses and REST models (Ctrl+E)", ParticipantsAsync);
            if (_Selected != null)
            {
                Add("Start a debate in this conversation (Ctrl+B)", StartAsync);
                Add("Run controls: pause, resume, interrupt or stop (Ctrl+R)", ControlsAsync);
                Add("Stop this debate (F8; s in list)", StopAsync);
                Add("Read full message history (Ctrl+L)", HistoryAsync);
                Add("Inspect generation attempts (Ctrl+G)", AttemptsAsync);
                Add("Ask an agent a follow-up question (Ctrl+Y)", FollowupAsync);
                Add("Delete this conversation (d / Delete in list)", DeleteConversationAsync);
            }
            if (_Page > 1) Add("Previous conversation page", () => UiAsync(() => { ChangePage(-1); return Task.FromResult(true); }));
            if (_Page < _TotalPages) Add("Next conversation page", () => UiAsync(() => { ChangePage(1); return Task.FromResult(true); }));
            Add("Search conversations and archived history (Ctrl+F)", SearchAsync);
            Add("Switch light / dark theme", () => { _App.Post(ToggleTheme); return Task.CompletedTask; });
            Add("Help: how to use ArgueBot", HelpAsync);
            Add("Detach (the service keeps running)", () => { _App.Post(_App.Quit); return Task.CompletedTask; });
            int selected = await UiAsync(() => _App.SelectStyledAsync("Main menu: choose an action", labels.ToArray())).ConfigureAwait(false);
            if (selected >= 0) await actions[selected]().ConfigureAwait(false);
        }
        private async Task HelpAsync()
        {
            if (_App == null) return;
            while (true)
            {
                int selected = await UiAsync(() => _App.SelectStyledAsync("Help", TerminalHelp.Titles)).ConfigureAwait(false);
                if (selected < 0) return;
                await UiAsync(() => _App.ShowAsync(new MessageDialog(new Message { Content = TerminalHelp.Pages[selected], Speaker = "Help" }) { Title = TerminalHelp.Titles[selected] })).ConfigureAwait(false);
            }
        }
    }
}
