namespace ArgueBot.Tui
{
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
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
        private readonly Dictionary<string, string> _ContextKeys = new Dictionary<string, string>();
        private readonly ButtonRow _Actions = new ButtonRow();
        private readonly ConversationListView _Threads = new ConversationListView();
        private readonly Pane _Transcript = new Pane("transcript");
        private readonly TextField _Composer = new TextField { Placeholder = "Context for the debate. Use Follow-up to ask one agent." };
        private readonly Label _ConversationHeading = new Label(Text.From("Conversations"));
        private readonly Label _NoteHeading = new Label(Text.From("Add context: Enter sends"));
        private readonly Label _HistoryTitle = new Label(Text.From("Shared debate history"));
        private readonly Label _Feedback = new Label(Text.From("Connecting to the local service..."));
        private int _ActionInProgress;
        private DateTime _FeedbackUntil;
        private readonly StatusBar _Status = new StatusBar { RightText = "ArgueBot", ReservedHint = KeyHint.For("f1", "Help") };
        /// <summary>The signed-in user, tenant and server, shown in the status bar.</summary>
        internal string Identity { get { return _Session?.Describe() ?? "ArgueBot"; } }
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
        private readonly TuiSession? _Session;
        private readonly bool _ShowSplash;
        /// <summary>Why the workspace closed.</summary>
        internal TuiExit Exit { get; private set; } = TuiExit.Quit;
        internal ConversationApp(ArgueBotClient client, TuiSession? session = null, bool showSplash = true) { _Client = client; _Session = session; _ShowSplash = showSplash; }
        /// <summary>Close the workspace for a reason that returns to the sign-in page.</summary>
        private void EndSession(TuiExit reason) { Exit = reason; _App?.Post(_App.Quit); }
        private AccountWorkflow Account() { return new AccountWorkflow(_Client, _Session ?? throw new InvalidOperationException("Sign in first."), _App!, _Dispatcher ?? throw new InvalidOperationException("Terminal dispatcher is unavailable."), _Token); }
        /// <summary>Refresh a session token when it has less than ten minutes left, so a long-running TUI stays signed in.</summary>
        private async Task KeepSessionAsync(CancellationToken token)
        {
            if (_Session == null || !_Session.IsSession || !_Session.ExpiresUtc.HasValue || _Session.ExpiresUtc.Value - DateTime.UtcNow > TimeSpan.FromMinutes(10)) return;
            TokenResponse refreshed = await _Client.RefreshAsync(token).ConfigureAwait(false);
            _Session.Remember(refreshed.Token, refreshed.ExpiresUtc, _Session.Persist);
        }
        internal async Task RunAsync(CancellationToken token)
        {
            using (CancellationTokenSource polling = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                _Token = polling.Token;
                Task poll = Task.CompletedTask;
                try
                {
                    await TuiApp.RunAsync(app => { Configure(app); poll = PollAsync(polling.Token); if (_ShowSplash) app.Post(() => Launch(() => SplashScreen.ShowAsync(app, polling.Token))); }, token).ConfigureAwait(false);
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
            _Actions.Add("Agents", () => Launch(ParticipantsAsync)).TooltipText = "Add or edit the agents used as debate speakers: CLI programs with an optional secret, or HTTP models.";
            _Actions.Add("Start debate", () => Launch(StartAsync)).TooltipText = "Choose speakers and settings for the selected conversation; review before starting.";
            _Actions.Add("Follow-up", () => Launch(FollowupAsync)).TooltipText = "Ask an agent another question using this conversation history (Ctrl+Y).";
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
            _NoteHeading.Content = Text.From("Add context: Enter sends", _App.Theme.Muted);
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
                try { await KeepSessionAsync(token).ConfigureAwait(false); await RefreshAsync(token).ConfigureAwait(false); }
                catch (DomainException ex) when (ex.StatusCode == 401) { EndSession(TuiExit.SessionEnded); return; }
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
                Dictionary<string, string> failures = new Dictionary<string, string>();
                if (selected != null)
                {
                    foreach (string runId in history.Items.Where(message => (message.State == "failed" || message.State == "interrupted") && message.RunId != null).Select(message => message.RunId!).Distinct())
                    {
                        HashSet<string> needed = history.Items.Where(message => message.RunId == runId && (message.State == "failed" || message.State == "interrupted")).Select(message => message.Id).ToHashSet();
                        try
                        {
                            int attemptPage = 1;
                            while (needed.Count > 0)
                            {
                                AttemptPage attempts = await _Client.AttemptsAsync(selected, runId, attemptPage, 100, token).ConfigureAwait(false);
                                foreach (GenerationAttempt attempt in attempts.Items.Where(attempt => attempt.State == "failed" || attempt.State == "interrupted"))
                                {
                                    if (needed.Remove(attempt.MessageId)) failures[attempt.MessageId] = attempt.ErrorMessage;
                                }
                                if ((long)attemptPage * attempts.PageSize >= attempts.TotalCount) break;
                                attemptPage++;
                            }
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException)
                        {
                            foreach (string messageId in needed) failures[messageId] = "This turn failed. Failure details are unavailable; check the service connection and press Ctrl+G to inspect generation attempts before retrying.";
                        }
                    }
                }
                string content = (selected ?? "welcome") + "\n" + title + "\n" + string.Join("\n\n", history.Items.Select(message => message.Speaker + " | " + message.Kind + " | " + message.State + "\n" + message.Content + "\n" + message.ContentTruncated + "\n" + (failures.TryGetValue(message.Id, out string? explanation) ? explanation : "")));
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
                    if (_LastContent != content) { TranscriptFormatter.Render(_Transcript, history.Items, _App!.Theme, title, failures); _LastContent = content; }
                    if (DateTime.UtcNow >= _FeedbackUntil) _Feedback.Content = Text.From((_Session == null ? "Connected" : _Session.ShortIdentity()) + " | " + page.TotalCount + (page.TotalCount == 1 ? " conversation" : " conversations") + " | Page " + _Page + "/" + _TotalPages + " | " + history.TotalCount + " messages", _App!.Theme.Muted);
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
            catch (DomainException ex) when (ex.StatusCode == 401 && _Session != null) { EndSession(TuiExit.SessionEnded); }
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
            string[] choices = new[] { "Add agent (CLI program or HTTP model)" }.Concat(profiles.Select(profile => profile.Name + " [" + profile.Kind + "/" + profile.Provider + "]" + (profile.Enabled ? "" : " disabled"))).Concat(new[] { "Connections, models and credentials" }).ToArray();
            int selected = await UiAsync(() => _App.SelectStyledAsync("Agents: choose Add agent, or select an existing agent", choices)).ConfigureAwait(false);
            if (selected < 0) return;
            if (selected == profiles.Count + 1) { await CatalogAsync().ConfigureAwait(false); return; }
            RunnerProfile? profile = selected == 0 ? null : profiles[selected - 1];
            if (profile != null)
            {
                int action = await UiAsync(() => _App.SelectStyledAsync(profile.Name, "Edit agent", "Delete reusable profile (history remains)", "Check agent (runs it once; may incur cost)")).ConfigureAwait(false);
                if (action < 0) return;
                if (action == 2) { await ChecksAsync(profile.Id, profile.Name, true).ConfigureAwait(false); return; }
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
            List<ModelProfile> models = await _Client.ModelsAsync(_Token).ConfigureAwait(false);
            List<CredentialMetadata> secrets = await _Client.CredentialsAsync(_Token).ConfigureAwait(false);
            RunnerProfile? updated = await UiAsync(async () =>
            {
                ParticipantEditor editor = new ParticipantEditor(profile ?? preset, models, secrets);
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
        private static string CredentialKindLabel(string kind) { return kind == "claude_oauth" ? "Claude Code OAuth token" : kind == "codex_auth_json" ? "Codex auth.json" : kind == "http_bearer" ? "Bearer token" : kind == "http_header" ? "Header key and value" : kind == "http_query" ? "Query string key and value" : "HTTP token or API key"; }
        private static string CredentialStatusLabel(string status) { return status == "ready" ? "ready" : status == "disabled" ? "disabled" : status == "refresh_uncertain" ? "sign-in needed: refresh result unknown, replace auth.json" : status; }
        /// <summary>Default prompts, or one chosen prompt per debate step. Null means the moderator cancelled.</summary>
        private async Task<Dictionary<string, string>?> ChoosePromptsAsync()
        {
            Dictionary<string, string> chosen = new Dictionary<string, string>(StringComparer.Ordinal);
            List<PromptTemplate> prompts = await _Client.PromptsAsync(token: _Token).ConfigureAwait(false);
            if (prompts.All(item => item.Factory)) return chosen;
            int mode = await UiAsync(() => _App!.SelectStyledAsync("Debate prompts for this conversation", "Use the factory default prompts", "Choose a prompt for each debate step")).ConfigureAwait(false);
            if (mode < 0) return null;
            if (mode == 0) return chosen;
            foreach (string purpose in PromptCatalog.Purposes)
            {
                List<PromptTemplate> options = prompts.Where(item => item.Purpose == purpose).ToList();
                if (options.Count < 2) continue;
                int choice = await UiAsync(() => _App!.SelectStyledAsync("Prompt for the " + purpose + " step", options.Select(item => item.Name + (item.Factory ? " (factory)" : "")).ToArray())).ConfigureAwait(false);
                if (choice < 0) return null;
                chosen[purpose] = options[choice].Id;
            }
            return chosen;
        }
        private async Task PromptsAsync()
        {
            while (true)
            {
                List<PromptTemplate> prompts = await _Client.PromptsAsync(token: _Token).ConfigureAwait(false);
                int selected = await UiAsync(() => _App!.SelectStyledAsync("Prompts (factory defaults are read-only)", prompts.Select(item => item.Name + " [" + item.Purpose + (item.Factory ? ", factory" : "") + "]").Concat(new[] { "Back" }).ToArray())).ConfigureAwait(false);
                if (selected < 0 || selected >= prompts.Count) return;
                PromptTemplate prompt = prompts[selected];
                string[] actions = prompt.Factory ? new[] { "View text and parameters", "Duplicate to an editable copy", "Back" } : new[] { "View text and parameters", "Duplicate", "Edit name, description or text", "Delete", "Back" };
                int action = await UiAsync(() => _App!.SelectStyledAsync(prompt.Name, actions)).ConfigureAwait(false);
                if (action < 0 || actions[action] == "Back") continue;
                try
                {
                    if (action == 0)
                    {
                        List<PromptParameter> parameters = await _Client.PromptParametersAsync(_Token).ConfigureAwait(false);
                        string legend = string.Join("\n", parameters.Where(item => item.Purposes.Contains(prompt.Purpose)).Select(item => "{" + item.Name + "}" + (item.RequiredFor.Contains(prompt.Purpose) ? " (required)" : "") + ": " + item.Description));
                        await ReadPanelAsync(prompt.Name, prompt.Body + "\n\nParameters for " + prompt.Purpose + " prompts:\n" + legend).ConfigureAwait(false);
                    }
                    else if (action == 1) { PromptTemplate copy = await _Client.DuplicatePromptAsync(prompt.Id, _Token).ConfigureAwait(false); await ReadPanelAsync("Prompt duplicated", "Created " + copy.Name + ". Choose it in the list to edit it.").ConfigureAwait(false); }
                    else if (action == 2) await EditCatalogAsync("Edit prompt", CatalogForms.Prompt(prompt), async value => { await _Client.SavePromptAsync(value, _Token).ConfigureAwait(false); }).ConfigureAwait(false);
                    else if (action == 3 && await UiAsync(() => _App!.ConfirmStyledAsync("Delete " + prompt.Name + "? Conversations using it switch to the factory default; started debates keep their copy.")).ConfigureAwait(false)) await _Client.DeletePromptAsync(prompt.Id, prompt.Revision, _Token).ConfigureAwait(false);
                }
                catch (DomainException exception) { await ReadPanelAsync("Prompt not changed", exception.Message).ConfigureAwait(false); }
            }
        }
        private async Task CatalogAsync()
        {
            int kind = await UiAsync(() => _App!.SelectStyledAsync("Connections and models", "HTTP endpoints", "Models (on HTTP endpoints)", "Secrets (write-only)", "Secret encryption keys")).ConfigureAwait(false);
            if (kind < 0) return;
            if (kind == 3)
            {
                CredentialKeyStatus status = await _Client.CredentialKeysAsync(_Token).ConfigureAwait(false);
                int action = await UiAsync(() => _App!.SelectStyledAsync("Secret encryption: active " + status.ActiveKeyId + ", " + status.RetainedKeyCount + (status.RetainedKeyCount == 1 ? " key" : " keys"), "Rotate master key and reencrypt stored secrets", status.RetainedKeyCount > 1 ? "Retire earlier keys (older backups lose their credentials)" : "Retire earlier keys (none to retire)", "Back")).ConfigureAwait(false);
                if (action == 0)
                {
                    if (!await UiAsync(() => _App!.ConfirmStyledAsync("Rotate the master key? Secret values and revisions stay the same. Previous keys remain for recovery until you retire them. Back up the protected secrets directory after rotation.")).ConfigureAwait(false)) return;
                    CredentialKeyStatus rotated = await _Client.RotateCredentialKeyAsync(status.ActiveKeyId, _Token).ConfigureAwait(false);
                    await ReadPanelAsync("Secret key rotated", rotated.ReencryptedCredentials + " secrets reencrypted." + (rotated.SkippedCredentials > 0 ? " " + rotated.SkippedCredentials + " disabled credentials could not be read and were left unchanged; replace them to include them." : "") + " Active key: " + rotated.ActiveKeyId + "\nBack up the protected secrets directory separately from the database.").ConfigureAwait(false);
                }
                else if (action == 1 && status.RetainedKeyCount > 1)
                {
                    if (!await UiAsync(() => _App!.ConfirmStyledAsync("Retire " + (status.RetainedKeyCount - 1) + " earlier keys? Database backups made before the latest rotation will no longer be able to decrypt their credentials with this keyring. Every current credential already uses the active key or this is refused.", "Retire earlier keys", "Keep them")).ConfigureAwait(false)) return;
                    CredentialKeyStatus retired = await _Client.RetireCredentialKeysAsync(status.ActiveKeyId, _Token).ConfigureAwait(false);
                    await ReadPanelAsync("Earlier keys retired", retired.RetiredKeyCount + " earlier keys removed. One active key remains: " + retired.ActiveKeyId + "\nBack up the protected secrets directory now.").ConfigureAwait(false);
                }
                return;
            }
            List<CredentialMetadata> credentials = await _Client.CredentialsAsync(_Token).ConfigureAwait(false);
            if (kind == 0)
            {
                List<EndpointProfile> items = await _Client.EndpointsAsync(_Token).ConfigureAwait(false);
                int selected = await ChooseCatalogAsync(items.Select(item => item.Name + (item.Enabled ? "" : " (disabled)"))).ConfigureAwait(false); if (selected < 0) return;
                if (selected > 0)
                {
                    int action = await UiAsync(() => _App!.SelectStyledAsync(items[selected - 1].Name, "Edit endpoint", "Check connection / list models (contacts endpoint; no completion)", "Preview requests (no endpoint contact)", "Back")).ConfigureAwait(false);
                    if (action < 0 || action == 3) return;
                    if (action == 2)
                    {
                        EndpointPreview preview = await _Client.PreviewEndpointAsync(items[selected - 1].Id, _Token).ConfigureAwait(false);
                        await ReadPanelAsync("Request preview", "Saved configuration only; no endpoint contacted or secret resolved. All values are redacted.\n\nPOST " + preview.GenerationUrl + "\n\nGET " + preview.DiscoveryUrl + "\n\nConfigured HTTP headers:\n\n" + (preview.HeaderNames.Count == 0 ? "None" : string.Join("\n\n", preview.HeaderNames.Select(name => name + ": [redacted]"))) + "\n\nConfigured query parameters:\n\n" + (preview.QueryNames.Count == 0 ? "None" : string.Join("\n\n", preview.QueryNames.Select(name => name + "=[redacted]"))) + "\n\nStandard protocol headers are added by the transport.").ConfigureAwait(false);
                        return;
                    }
                    if (action == 1)
                    {
                        EndpointDiscovery discovery = await _Client.DiscoverEndpointAsync(items[selected - 1].Id, _Token).ConfigureAwait(false);
                        await UiAsync(() => _App!.SelectStyledAsync("Connection succeeded; advertised models (capabilities unverified)", discovery.Models.Count == 0 ? new[] { "No advertised models; enter an ID manually" } : discovery.Models.Concat(discovery.Truncated ? new[] { "More models omitted; enter an ID manually" } : Array.Empty<string>()).ToArray())).ConfigureAwait(false);
                        return;
                    }
                }
                await EditCatalogAsync("HTTP endpoint", CatalogForms.Endpoint(selected == 0 ? new EndpointProfile() : items[selected - 1], credentials), async value => { await _Client.SaveEndpointAsync(value, _Token).ConfigureAwait(false); }).ConfigureAwait(false);
            }
            else if (kind == 1)
            {
                List<ModelProfile> items = await _Client.ModelsAsync(_Token).ConfigureAwait(false);
                int selected = await ChooseCatalogAsync(items.Select(item => item.Name + (item.Enabled ? "" : " (disabled)"))).ConfigureAwait(false); if (selected < 0) return;
                if (selected > 0)
                {
                    int action = await UiAsync(() => _App!.SelectStyledAsync(items[selected - 1].Name, "Edit model", "Observed completion and tool checks", "Back")).ConfigureAwait(false);
                    if (action < 0 || action == 2) return;
                    if (action == 1) { await ModelChecksAsync(items[selected - 1]).ConfigureAwait(false); return; }
                }
                List<EndpointProfile> endpoints = await _Client.EndpointsAsync(_Token).ConfigureAwait(false);
                await EditCatalogAsync("Reusable model", CatalogForms.Model(selected == 0 ? new ModelProfile() : items[selected - 1], endpoints), async value => { await _Client.SaveModelAsync(value, _Token).ConfigureAwait(false); }).ConfigureAwait(false);
            }
            else
            {
                int selected = await ChooseCatalogAsync(credentials.Select(item => item.Name + " [" + CredentialKindLabel(item.Kind) + "] " + CredentialStatusLabel(item.Status))).ConfigureAwait(false); if (selected < 0) return;
                CredentialMetadata? item = selected == 0 ? null : credentials[selected - 1];
                if (item != null)
                {
                    int action = await UiAsync(() => _App!.SelectStyledAsync(item.Name, "Replace value", "Reveal value", "Disable secret", "Back")).ConfigureAwait(false); if (action < 0 || action == 3) return;
                    if (action == 1)
                    {
                        int confirm = await UiAsync(() => _App!.SelectStyledAsync("Show " + item.Name + " on screen? Anyone who can see this terminal can read it.", "Cancel", "Reveal")).ConfigureAwait(false); if (confirm != 1) return;
                        RevealedCredential revealed = await _Client.RevealCredentialAsync(item.Id, _Token).ConfigureAwait(false);
                        await ReadPanelAsync("Secret value", (revealed.ParameterName.Length > 0 ? revealed.ParameterName + ": " : "") + revealed.SecretValue).ConfigureAwait(false);
                        revealed.SecretValue = "";
                        return;
                    }
                    if (action == 2) { await _Client.DisableCredentialAsync(item.Id, item.Revision, _Token).ConfigureAwait(false); return; }
                }
                await EditCatalogAsync("Import secret (write-only)", CatalogForms.Credential(item), async value => { await _Client.SaveCredentialAsync(item?.Id ?? "", value, _Token).ConfigureAwait(false); value.SecretValue = ""; }).ConfigureAwait(false);
            }
        }
        private Task ModelChecksAsync(ModelProfile model) { return ChecksAsync(model.Id, model.Name, false); }
        /// <summary>Observed checks of a reusable model, or of a saved agent (which runs with its own secret, model and arguments).</summary>
        private async Task ChecksAsync(string id, string name, bool agent)
        {
            int page = 1;
            while (true)
            {
                List<ModelCheck> checks = await (agent ? _Client.AgentChecksAsync(id, page, _Token) : _Client.ModelChecksAsync(id, page, _Token)).ConfigureAwait(false);
                List<string> choices = new List<string> { "Test completion (invokes model; may incur cost)", "Test completion and harmless tool round trip (may incur cost)" };
                choices.AddRange(checks.Select(check => check.State + ": model revision " + check.ModelRevision + ", transport revision " + check.TransportRevision + " / " + check.CreatedUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture)));
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous checks"); }
                if (checks.Count == 25) { next = choices.Count; choices.Add("Next checks"); }
                int back = choices.Count; choices.Add("Back");
                int selected = await UiAsync(() => _App!.SelectStyledAsync(name + ": observed checks, page " + page, choices.ToArray())).ConfigureAwait(false);
                if (selected < 0 || selected == back) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                ModelCheck result;
                if (selected < 2)
                {
                    if (!await UiAsync(() => _App!.ConfirmStyledAsync("Invoke this model? This may incur provider cost. A tool check can require a second completion. CLI agent checks use the agent's own arguments and permissions. Check output is discarded; only safe capability observations are retained.")).ConfigureAwait(false)) continue;
                    ModelCheckRequest request = new ModelCheckRequest { CheckTools = selected == 1, IdempotencyKey = Guid.NewGuid().ToString("N") };
                    result = await (agent ? _Client.CheckAgentAsync(id, request, _Token) : _Client.CheckModelAsync(id, request, _Token)).ConfigureAwait(false);
                }
                else result = checks[selected - 2];
                await ReadPanelAsync("Observed model check", "State: " + result.State + "\n\nCompletion observed: " + result.CompletionVerified + "\n\nTool round trip observed: " + result.ToolsVerified + "\n\nModel revision: " + result.ModelRevision + "\n\nTransport revision: " + result.TransportRevision + "\n\nExecutable version: " + (result.ExecutableVersion.Length == 0 ? "Unverified" : result.ExecutableVersion) + "\n\nSafe error code: " + result.ErrorCode + "\n\nA successful check does not certify debate quality or every model capability.").ConfigureAwait(false);
            }
        }
        private Task<int> ChooseCatalogAsync(IEnumerable<string> names)
        { return UiAsync(() => _App!.SelectStyledAsync("Add or edit configuration", new[] { "Add new configuration" }.Concat(names).ToArray())); }
        private Task<T?> EditCatalogAsync<T>(string title, CatalogEditor<T> editor, Func<T, Task> submit) where T : class
        {
            return UiAsync(async () =>
            {
                FormDialog<T> dialog = new FormDialog<T>(title, editor.Form, editor.Extract, _App, submit); dialog.ApplyTheme(_App!.Theme);
                return await _App.ShowAsync<T>(dialog).ConfigureAwait(false);
            });
        }
        private async Task NewAsync()
        {
            if (_App == null) return;
            while (true)
            {
                int action = await UiAsync(() => _App.SelectStyledAsync("New: what would you like to do?", "Start a debate: guided setup", "Create an empty conversation (start a debate later)", "Set up agents first (CLI programs or HTTP models)")).ConfigureAwait(false);
                if (action < 0) return;
                if (action == 2) { await ParticipantsAsync().ConfigureAwait(false); continue; }
                if (action == 0) { await ConfigureDebateAsync(null).ConfigureAwait(false); return; }
                string? title = await UiAsync(() => _App.PromptStyledAsync("Conversation title (shown in the conversation list)")).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(title)) return;
                string? question = await UiAsync(() => _App.PromptStyledAsync("Opening question for the debate (optional; blank uses the title)")).ConfigureAwait(false);
                Dictionary<string, string>? promptIds = await ChoosePromptsAsync().ConfigureAwait(false);
                if (promptIds == null) return;
                Conversation conversation = await _Client.CreateConversationAsync(title, _Token, question?.Trim() ?? "", promptIds).ConfigureAwait(false);
                await UiAsync(() => { OpenConversation(conversation.Id, conversation.Title); return Task.FromResult(true); }).ConfigureAwait(false);
                return;
            }
        }
        private async Task SendAsync()
        {
            if (_Selected == null) throw new InvalidOperationException("Create or open a conversation first.");
            string text = _Composer.Value; if (string.IsNullOrWhiteSpace(text)) return;
            string conversationId = _Selected;
            DebateRun? run = (await _Client.RunsAsync(conversationId, _Token).ConfigureAwait(false)).LastOrDefault();
            if (run?.ProtocolVersion == 2)
            {
                int mode = await UiAsync(() => _App!.SelectStyledAsync("When should this context take effect?", "Use in next turn (current answer finishes)", "Interrupt now (stop the current answer)", "Use in next turn and pin", "Interrupt now and pin", "Cancel: keep my draft")).ConfigureAwait(false);
                if (mode < 0 || mode == 4) return;
                await SendInterjectionAsync(conversationId, new InterjectionRequest { RunId = run.Id, Text = text, DeliveryMode = mode == 1 || mode == 3 ? "interrupt_now" : "next_turn", Pinned = mode >= 2 }, _Token).ConfigureAwait(false);
            }
            else await _Client.NoteAsync(conversationId, text, token: _Token).ConfigureAwait(false);
            _App?.Post(() => { if (_Selected == conversationId && _Composer.Value == text) _Composer.Value = ""; if (_Drafts.TryGetValue(conversationId, out string? draft) && draft == text) _Drafts.Remove(conversationId); });
        }
        private async Task SendInterjectionAsync(string conversationId, InterjectionRequest request, CancellationToken token)
        {
            request.IdempotencyKey = "";
            string identity = conversationId + ":" + System.Text.Json.JsonSerializer.Serialize(request);
            if (!_ContextKeys.TryGetValue(identity, out string? key)) { key = Guid.NewGuid().ToString("N"); _ContextKeys[identity] = key; }
            request.IdempotencyKey = key;
            await _Client.InterjectAsync(conversationId, request, token).ConfigureAwait(false);
            _ContextKeys.Remove(identity);
        }
        private async Task StartAsync()
        {
            await ConfigureDebateAsync(_Selected).ConfigureAwait(false);
        }
        private async Task ConfigureDebateAsync(string? conversationId)
        {
            if (_App == null) return;
            // Seats are agents, each carrying its own optional secret; see SeatCatalog.
            List<RunnerProfile> profiles = await SeatCatalog.LoadAsync(_Client, _Token).ConfigureAwait(false);
            while (profiles.Count(profile => profile.Enabled) < 2)
            {
                int action = await UiAsync(() => _App.SelectStyledAsync("Set up agents: " + profiles.Count(profile => profile.Enabled) + " of 2 enabled", "Add or enable an agent (Claude Code, Codex, Gemini, Mux or an HTTP model)", "Cancel debate setup")).ConfigureAwait(false);
                if (action == 0) await ParticipantsAsync().ConfigureAwait(false);
                else return;
                profiles = await SeatCatalog.LoadAsync(_Client, _Token).ConfigureAwait(false);
            }
            string question = "";
            if (conversationId != null)
            {
                List<DebateRun> previous = await _Client.RunsAsync(conversationId, _Token).ConfigureAwait(false);
                question = previous.FirstOrDefault(run => !string.IsNullOrWhiteSpace(run.Question))?.Question ?? (await _Client.ConversationAsync(conversationId, _Token).ConfigureAwait(false)).Title;
            }
            ServiceCapabilities capabilities = await _Client.CapabilitiesAsync(_Token).ConfigureAwait(false);
            CreateRunRequest? request = await UiAsync(() =>
            {
                DebateSetupEditor editor = new DebateSetupEditor(profiles, question, conversationId != null, capabilities.MaxParticipants);
                return _App.ShowAsync<CreateRunRequest>(new DebateWizardDialog(editor, _App, async candidate =>
                {
                    if (conversationId == null)
                    {
                        Conversation conversation = await _Client.CreateConversationAsync(candidate.Question, _Token, candidate.Question).ConfigureAwait(false);
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
            List<RunnerProfile> profiles = (await SeatCatalog.LoadAsync(_Client, _Token).ConfigureAwait(false)).Where(profile => profile.Enabled).ToList();
            if (profiles.Count == 0) throw new InvalidOperationException("Add an enabled agent first (Menu > Agents).");
            int index = await UiAsync(() => _App.SelectStyledAsync("Choose follow-up respondent", profiles.Select(profile => profile.Name + " (" + profile.Kind + ")").ToArray())).ConfigureAwait(false);
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
            string[] actions = new string[] { "pause", "resume", "interrupt", "continue", "extend", "steer", "stop", "roster", "proposals", "context", "memory", "clarifications", "rebuild_summary", "exclude_summary", "usage", "edit_proposal" };
            string[] commandLabels = new[] { "Pause after turn", "Resume interrupted run (retry may incur cost)", "Interrupt current generation", "Continue immediately", "Extend by two rounds", "Private directive to a participant", "Stop run", "Change agents", "Inspect proposals and positions", "Inspect context delivery", "Inspect summaries and original sources", "Answer a clarification", "Rebuild summary (pause first)", "Exclude summary (pause first)", "Inspect execution usage and limits", "Edit the proposal under vote" };
            string[] groupNames = new[] { "Run", "Steer the debate", "Inspect", "Working memory" };
            int[][] groups = new[] { new[] { 0, 1, 3, 2, 4, 6 }, new[] { 5, 15, 7, 11 }, new[] { 8, 9, 10, 14 }, new[] { 12, 13 }, };
            List<string> display = new List<string>(); List<int> mapping = new List<int>();
            for (int group = 0; group < groups.Length; group++)
            {
                display.Add(ChoiceDialog.Heading + groupNames[group]); mapping.Add(-1);
                foreach (int entry in groups[group]) { display.Add(commandLabels[entry]); mapping.Add(entry); }
            }
            int chosen = await UiAsync(() => _App.SelectStyledAsync("Run " + run.State + (run.Outcome.Length > 0 ? " | " + run.Outcome : "") + ": choose a command", display.ToArray())).ConfigureAwait(false);
            if (chosen < 0 || mapping[chosen] < 0) return;
            int selected = mapping[chosen];
            if (selected >= 7)
            {
                if (run.ProtocolVersion != 2) throw new InvalidOperationException("These controls require a model-guided debate. Historical runs remain available in history.");
                if (selected == 7) await ChangeAgentsAsync(conversationId, run).ConfigureAwait(false);
                else if (selected == 8)
                {
                    List<DebateProposal> proposals = await _Client.ProposalsAsync(conversationId, run.Id, _Token).ConfigureAwait(false);
                    List<DebateSeat> seats = await _Client.RosterAsync(conversationId, run.Id, _Token).ConfigureAwait(false);
                    string content = proposals.Count == 0 ? "The orchestrator has not proposed an answer yet." : string.Join("\n\n", proposals.Select((proposal, index) => "## Proposal " + (index + 1) + " of " + proposals.Count + (proposal.ModeratorEdited ? " (your revision)" : "") + (proposal.Superseded ? " (replaced by a revision; its votes do not count)" : "") + "\n\n" + proposal.Content + "\n\n" + proposal.Agreements.Count(vote => !vote.Superseded && vote.Position == "agree") + " of " + proposal.Agreements.Count(vote => !vote.Superseded) + " current votes agree. Context revision " + proposal.ContextEpoch + ", roster revision " + proposal.RosterRevision + ".\n\n" + string.Join("\n", proposal.Agreements.OrderBy(vote => vote.Superseded).Select(vote => "- " + (seats.FirstOrDefault(seat => seat.ParticipantId == vote.ParticipantId)?.Name ?? vote.ParticipantId) + ": **" + vote.Position + "**" + (vote.Superseded ? " (earlier vote, superseded)" : "") + ". " + vote.Rationale))));
                    await ReadPanelAsync("Proposals and positions", content).ConfigureAwait(false);
                }
                else if (selected == 9)
                {
                    List<InterjectionReceipt> receipts = await _Client.InterjectionsAsync(conversationId, token: _Token).ConfigureAwait(false);
                    await ReadPanelAsync("Context delivery", receipts.Count == 0 ? "No context has been submitted yet." : string.Join("\n\n", receipts.Select(receipt => "- Context " + receipt.ContextEpoch + ": " + receipt.DeliveryMode + ", " + receipt.Status + ". Original delivered to " + receipt.DeliveredParticipantIds.Count + " agents; represented by a source-linked summary for " + receipt.SummarizedParticipantIds.Count + " others. Source: " + receipt.MessageId))).ConfigureAwait(false);
                }
                else if (selected == 10) await InspectMemoryAsync(conversationId, run).ConfigureAwait(false);
                else if (selected == 11) await AnswerClarificationAsync(conversationId, run).ConfigureAwait(false);
                else if (selected == 15) await EditProposalAsync(conversationId, run).ConfigureAwait(false);
                else if (selected == 14) await ReadPanelAsync("Execution usage and limits", "Participant invocations: " + run.Usage.ParticipantTurns + "/" + run.Policy.MaxParticipantTurns + "\n\nOrchestration: " + run.Usage.OrchestrationCalls + "/" + run.Policy.MaxOrchestrationCalls + "\n\nCompaction: " + run.Usage.CompactionCalls + "/" + run.Policy.MaxCompactionCalls + "\n\nTools: " + run.Usage.ToolCalls + "/" + run.Policy.MaxToolCalls + "\n\nEstimated initial-input tokens: " + run.Usage.EstimatedInputTokens + "/" + run.Policy.MaxEstimatedInputTokens + " (0 means no limit).\n\nEstimate is UTF-8 bytes plus framing, not billed tokens; excludes continuations/output. No currency cap.\n\nElapsed deadline includes paused time: " + (run.Policy.MaxWallClockSeconds > 0 ? run.CreatedUtc.AddSeconds(run.Policy.MaxWallClockSeconds).ToString("O") : "Legacy run: no configured deadline")).ConfigureAwait(false);
                else
                {
                    bool accepted = await UiAsync(() => _App.ConfirmStyledAsync("Changing derived context invalidates the current proposal. Original turns remain intact. Continue?")).ConfigureAwait(false);
                    if (accepted) await _Client.ActionAsync(conversationId, run.Id, new CommandRequest { Action = actions[selected], Revision = run.Revision, IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
                }
                return;
            }
            CommandRequest command = new CommandRequest { Action = actions[selected], Revision = run.Revision, IdempotencyKey = Guid.NewGuid().ToString("N") };
            if (command.Action == "steer")
            {
                int recipient = await UiAsync(() => _App.SelectStyledAsync("Directive recipient (only this participant receives the text)", run.Participants.Select(profile => profile.Name).ToArray())).ConfigureAwait(false);
                if (recipient < 0) return;
                string? text = await UiAsync(() => _App.PromptStyledAsync("Private directive for " + run.Participants[recipient].Name)).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text)) return;
                command.RecipientId = run.Participants[recipient].Id; command.Text = text;
                if (run.ProtocolVersion == 2)
                {
                    int delivery = await UiAsync(() => _App.SelectStyledAsync("When should this directive take effect?", "Use in next turn (current answer finishes)", "Interrupt now (stop the current answer)")).ConfigureAwait(false);
                    if (delivery < 0) return;
                    await SendInterjectionAsync(conversationId, new InterjectionRequest { RunId = run.Id, Text = text, RecipientId = command.RecipientId, DeliveryMode = delivery == 0 ? "next_turn" : "interrupt_now", IdempotencyKey = command.IdempotencyKey }, _Token).ConfigureAwait(false);
                    return;
                }
            }
            if (command.Action == "stop" || command.Action == "interrupt")
            {
                bool accepted = await UiAsync(() => _App.ConfirmStyledAsync(command.Action == "stop" ? "Stop this run? Partial answers remain in history." : "Interrupt the current answer? Partial text remains; resumption retries the turn.")).ConfigureAwait(false);
                if (!accepted) return;
            }
            await _Client.ActionAsync(conversationId, run.Id, command, _Token).ConfigureAwait(false);
        }
        private async Task EditProposalAsync(string conversationId, DebateRun run)
        {
            if (run.Phase != "ratification" || run.ProposalId.Length == 0 || run.State == "completed" || run.State == "cancelled" || run.State == "failed")
            {
                await ReadPanelAsync("Edit proposal", "There is no proposal awaiting votes. You can edit a proposal while the panel is voting on it.").ConfigureAwait(false);
                return;
            }
            string? text = await UiAsync(() => _App!.PromptStyledAsync("Revised proposal (every seat votes again)", run.ProposalText)).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == run.ProposalText.Trim()) return;
            bool accepted = await UiAsync(() => _App!.ConfirmStyledAsync("Replace the proposal and restart voting? Votes already cast stay with the original as history, and a vote finishing now is not counted.", "Replace and revote", "Keep current proposal")).ConfigureAwait(false);
            if (!accepted) return;
            await _Client.ActionAsync(conversationId, run.Id, new CommandRequest { Action = "edit_proposal", Text = text, Revision = run.Revision, IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
        }
        private async Task InspectMemoryAsync(string conversationId, DebateRun run)
        {
            int page = 1;
            while (true)
            {
                List<ContextSummary> summaries = await _Client.SummariesAsync(conversationId, run.Id, page, _Token).ConfigureAwait(false);
                if (summaries.Count == 0 && page == 1)
                {
                    await ReadPanelAsync("Summaries", "No summaries have been created yet. ArgueBot compacts context when the next invocation approaches its configured capacity. Original turns remain available in conversation history.").ConfigureAwait(false);
                    return;
                }
                List<string> choices = summaries.Select(summary => (summary.Active ? "Active: " : "Historical: ") + (summary.RecipientId.Length == 0 ? "Shared: " : "Private: ") + summary.Content.Substring(0, Math.Min(70, summary.Content.Length))).ToList();
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if (summaries.Count == 25) { next = choices.Count; choices.Add("Next page"); }
                int back = choices.Count; choices.Add("Back");
                int selected = await UiAsync(() => _App!.SelectStyledAsync("Summaries, page " + page, choices.ToArray())).ConfigureAwait(false);
                if (selected < 0 || selected == back) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                ContextSummary summary = summaries[selected];
                await ReadPanelAsync("Summary", "Original sources remain authoritative.\n\n" + SummaryText(summary)).ConfigureAwait(false);
                while (true)
                {
                    List<string> sources = summary.SourceIds.ToList();
                    int sourcePrevious = -1; int sourceNext = -1;
                    if (summary.SourcePageNumber > 1) { sourcePrevious = sources.Count; sources.Add("Previous sources"); }
                    if ((long)summary.SourcePageNumber * 100 < summary.SourceCount) { sourceNext = sources.Count; sources.Add("Next sources"); }
                    int parentStart = sources.Count;
                    sources.AddRange(summary.ParentSummaryIds.Select(parentId => "Earlier summary: " + parentId));
                    int sourceBack = sources.Count; sources.Add("Back to summaries");
                    int source = await UiAsync(() => _App!.SelectStyledAsync("Original sources: page " + summary.SourcePageNumber + ", " + summary.SourceCount + " total", sources.ToArray())).ConfigureAwait(false);
                    if (source < 0 || source == sourceBack) break;
                    if (source >= parentStart && source < sourceBack)
                    {
                        summary = (await _Client.SummaryAsync(conversationId, run.Id, summary.ParentSummaryIds[source - parentStart], 1, _Token).ConfigureAwait(false)).Single();
                        await ReadPanelAsync("Earlier summary", "Original sources remain authoritative.\n\n" + SummaryText(summary)).ConfigureAwait(false);
                        continue;
                    }
                    if (source == sourcePrevious || source == sourceNext)
                    {
                        summary = (await _Client.SummaryAsync(conversationId, run.Id, summary.Id, summary.SourcePageNumber + (source == sourceNext ? 1 : -1), _Token).ConfigureAwait(false)).Single();
                        continue;
                    }
                    Message message = await _Client.MessageAsync(conversationId, summary.SourceIds[source], _Token).ConfigureAwait(false);
                    string digest = summary.Sources.FirstOrDefault(item => item.MessageId == message.Id)?.Digest ?? "Unavailable";
                    await ReadPanelAsync("Source " + message.Sequence + ": " + message.Speaker, "Content SHA-256 at summary acceptance: " + digest + "\n\n" + message.Content).ConfigureAwait(false);
                }
            }
        }
        private static string SummaryText(ContextSummary summary)
        {
            return (summary.RecipientId.Length == 0 ? "Shared memory" : "Private to " + summary.RecipientId) + "\n\nSummary: " + summary.Id + "\n\nGeneration attempt: " + summary.AttemptId + "\n\n" + summary.Content + "\n\nSource hashes identify original content; they do not establish summary accuracy. Earlier summaries are available in the source chooser.";
        }
        private async Task AnswerClarificationAsync(string conversationId, DebateRun run)
        {
            List<ClarificationRequest> requests = (await _Client.ClarificationsAsync(conversationId, run.Id, _Token).ConfigureAwait(false)).Where(item => item.Status == "open").ToList();
            if (requests.Count == 0) { await ReadPanelAsync("Clarifications", "No open questions from the panel.").ConfigureAwait(false); return; }
            int selected = await UiAsync(() => _App!.SelectStyledAsync("Choose a question to answer", requests.Select(item => (item.Blocking ? "Required: " : "Optional: ") + item.Question).ToArray())).ConfigureAwait(false);
            if (selected < 0) return;
            ClarificationRequest request = requests[selected];
            await ReadPanelAsync("Question", request.Question).ConfigureAwait(false);
            int action = await UiAsync(() => _App!.SelectStyledAsync("Respond to the panel", "Answer with supporting context", "Skip: answer is unavailable", "Back")).ConfigureAwait(false);
            if (action < 0 || action == 2) return;
            string? text = action == 1 ? "Moderator skipped this question: " + request.Question : await UiAsync(() => _App!.PromptStyledAsync("Supporting context (shared and pinned)")).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return;
            int delivery = await UiAsync(() => _App!.SelectStyledAsync("When should this answer take effect?", "Use in next turn", "Interrupt now")).ConfigureAwait(false);
            if (delivery < 0) return;
            await SendInterjectionAsync(conversationId, new InterjectionRequest { RunId = run.Id, Text = text, ClarificationId = request.Id, SkipClarification = action == 1, Pinned = true, DeliveryMode = delivery == 0 ? "next_turn" : "interrupt_now", IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
            await ReadPanelAsync("Context accepted", "Your response is recorded. If the debate is paused, choose Resume when you are ready.").ConfigureAwait(false);
        }
        private async Task ReadPanelAsync(string title, string content)
        {
            await UiAsync(async () =>
            {
                MessageDialog dialog = new MessageDialog(new Message { Speaker = "ArgueBot", Kind = title, Content = content });
                dialog.ApplyTheme(_App!.Theme);
                await _App.ShowAsync(dialog).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        private async Task ChangeAgentsAsync(string conversationId, DebateRun run)
        {
            int action = await UiAsync(() => _App!.SelectStyledAsync("Change the panel at a safe boundary", "Add an agent", "Remove an agent", "Replace an agent")).ConfigureAwait(false);
            if (action < 0) return;
            string seat = "";
            if (action != 0)
            {
                int index = await UiAsync(() => _App!.SelectStyledAsync("Choose the current agent", run.Participants.Select(profile => profile.Name).ToArray())).ConfigureAwait(false);
                if (index < 0) return;
                seat = run.Participants[index].Id;
            }
            string profileId = "";
            if (action != 1)
            {
                List<RunnerProfile> profiles = (await SeatCatalog.LoadAsync(_Client, _Token).ConfigureAwait(false)).Where(profile => profile.Enabled).ToList();
                int index = await UiAsync(() => _App!.SelectStyledAsync("Choose the new seat", profiles.Select(profile => profile.Name + " (" + profile.Kind + ")").ToArray())).ConfigureAwait(false);
                if (index < 0) return;
                profileId = profiles[index].Id;
            }
            bool accepted = await UiAsync(() => _App!.ConfirmStyledAsync("Apply this change between invocations? Earlier answers and dissent remain visible; every active agent must agree again.", "Apply change", "Cancel")).ConfigureAwait(false);
            if (!accepted) return;
            await _Client.ChangeRosterAsync(conversationId, run.Id, new RosterChangeRequest { Action = action == 0 ? "add" : action == 1 ? "remove" : "replace", ParticipantId = seat, ProfileId = profileId, Revision = run.Revision, IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
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
            List<string> labels = new List<string>(); List<Func<Task>?> actions = new List<Func<Task>?>();
            void Section(string title) { labels.Add(ChoiceDialog.Heading + title); actions.Add(null); }
            void Add(string label, Func<Task> action) { labels.Add(label); actions.Add(action); }
            if (_Selected != null)
            {
                string title = (_SelectedTitle ?? "this conversation").Trim();
                Section("This conversation: " + (title.Length > 40 ? title.Substring(0, 40) + "..." : title));
                Add("Start a debate here  (Ctrl+B)", StartAsync);
                Add("Run controls: pause, resume, interrupt, extend or stop  (Ctrl+R)", ControlsAsync);
                Add("Ask one agent a follow-up question  (Ctrl+Y)", FollowupAsync);
                Add("Read the full history  (Ctrl+L)", HistoryAsync);
                Add("Inspect generation attempts  (Ctrl+G)", AttemptsAsync);
                if (_Session != null) { string shareId = _Selected; string shareTitle = title; Add("Share a read-only link to this transcript", () => Account().ShareAsync(shareId, shareTitle)); }
                Add("Delete this conversation  (d in the list)", DeleteConversationAsync);
            }
            Section("Conversations");
            Add("New debate or conversation  (Ctrl+N)", NewAsync);
            Add("Search conversations, including archived  (Ctrl+F)", SearchAsync);
            if (_Page > 1) Add("Previous page of conversations  (Alt+Left)", () => UiAsync(() => { ChangePage(-1); return Task.FromResult(true); }));
            if (_Page < _TotalPages) Add("Next page of conversations  (Alt+Right)", () => UiAsync(() => { ChangePage(1); return Task.FromResult(true); }));
            Section("Set up");
            Add("Agents: who can take part in a debate  (Ctrl+E)", ParticipantsAsync);
            Add("Models, connections and secrets", CatalogAsync);
            Add("Prompts: the instructions each debate step sends", PromptsAsync);
            if (_Session != null)
            {
                Section("Account");
                Add("Who am I signed in as", () => Account().ShowAccountAsync());
                if (_Session.IsSession) Add("Change your password", () => Account().ChangePasswordAsync());
                Add("Your bearer tokens: create, disable or delete", () => Account().TokensAsync());
                Add("Your sessions: sign out other devices", () => Account().SessionsAsync());
                Add("Sign out", async () => { if (await UiAsync(() => _App.ConfirmStyledAsync("Sign out of " + _Session.Server + "?", "Sign out", "Cancel")).ConfigureAwait(false)) EndSession(TuiExit.SignOut); });
                Add("Switch server: sign in to another ArgueBot service", () => { EndSession(TuiExit.SwitchServer); return Task.CompletedTask; });
                if (_Session.IsTenantAdmin)
                {
                    Section("Administration");
                    Add("Users in " + _Session.Details.Tenant.Name, () => Account().UsersAsync());
                    if (_Session.IsSystemAdmin) Add("Tenants", () => Account().TenantsAsync());
                    Add("Audit log: sign-ins, changes and denied requests", () => Account().AuditAsync());
                }
            }
            Section("View and help");
            Add((_App.Theme.Name == "Dark" ? "Switch to the light theme" : "Switch to the dark theme") + "  (Ctrl+T)", () => { _App.Post(ToggleTheme); return Task.CompletedTask; });
            Add("Help  (F1)", HelpAsync);
            Add("Detach: close the TUI; debates keep running  (Ctrl+Q)", () => { _App.Post(_App.Quit); return Task.CompletedTask; });
            int selected = await UiAsync(() => _App.SelectStyledAsync("Menu", labels.ToArray())).ConfigureAwait(false);
            if (selected >= 0 && actions[selected] is Func<Task> action) await action().ConfigureAwait(false);
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
