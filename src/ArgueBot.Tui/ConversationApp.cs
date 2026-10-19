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
        private readonly ListView<string> _Threads = new ListView<string>();
        private Pane _Transcript = new Pane("transcript");
        private readonly TextField _Composer = new TextField { Placeholder = "Write a note; Enter sends. Ctrl+N creates a conversation. F1 explains controls." };
        private readonly StatusBar _Status = new StatusBar { RightText = "ArgueBot" };
        private List<Conversation> _Conversations = new List<Conversation>();
        private string? _Selected;
        private string _Search = "";
        private int _Page = 1;
        private bool _Archived;
        private string _LastContent = "";
        private TuiApplication? _App;
        private CancellationToken _Token;
        internal ConversationApp(ArgueBotClient client) { _Client = client; }
        internal async Task RunAsync(CancellationToken token)
        {
            _Token = token;
            using (CancellationTokenSource polling = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task poll = Task.CompletedTask;
                try
                {
                    await TuiApp.RunAsync(app => { Configure(app); poll = PollAsync(polling.Token); }, token).ConfigureAwait(false);
                }
                finally { polling.Cancel(); try { await poll.ConfigureAwait(false); } catch (OperationCanceledException) { } }
            }
        }
        internal void Configure(TuiApplication app)
        {
            _App = app; app.Theme = Theme.Dark; app.HighlightFocusedRegion = true;
            app.AddWidget("threads", _Threads, region => region.LeftAnchored(0, 28).FillHeight(0, 4).WithBorder(BorderStyle.Line, "Conversations: Enter opens").WithPadding(0));
            _Transcript = app.AddPane("transcript", region => region.FillWidth(28, 0).FillHeight(0, 4).WithBorder(BorderStyle.Line, "Shared debate history"));
            app.AddWidget("composer", _Composer, region => region.BottomAnchored(1, 3).FillWidth().WithBorder(BorderStyle.Line, "Note: Enter sends").WithPadding(0));
            app.AddWidget("status", _Status, region => region.BottomAnchored(0, 1).FillWidth().WithPadding(0));
            app.BindKeyHints(_Status).AddAppHint("ctrl+n", "New conversation").AddAppHint("ctrl+b", "Start debate").AddAppHint("ctrl+p", "Pause/resume").AddAppHint("ctrl+r", "Run controls").AddAppHint("ctrl+f", "Search").AddAppHint("ctrl+a", "Follow-up").AddAppHint("ctrl+e", "Participants").AddAppHint("ctrl+t", "Theme").AddAppHint("f1", "Help").AddAppHint("ctrl+q", "Quit");
            app.Bind("ctrl+q", app.Quit);
            app.Bind("ctrl+n", () => Launch(NewAsync));
            app.Bind("ctrl+b", () => Launch(StartAsync));
            app.Bind("ctrl+p", () => Launch(PauseAsync));
            app.Bind("ctrl+r", () => Launch(ControlsAsync));
            app.Bind("ctrl+a", () => Launch(FollowupAsync));
            app.Bind("ctrl+f", () => Launch(SearchAsync));
            app.Bind("ctrl+e", () => Launch(ParticipantsAsync));
            app.Bind("alt+right", () => _Page++);
            app.Bind("alt+left", () => _Page = Math.Max(1, _Page - 1));
            app.Bind("ctrl+t", () => app.Theme = app.Theme.Name == "Dark" ? Theme.Light : Theme.Dark);
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
                    List<Message> messages = selected == null ? new List<Message>() : await _Client.MessagesAsync(selected, token).ConfigureAwait(false);
                    string content = string.Join("\n\n", messages.Select(message => "[" + message.Speaker + " | " + message.Kind + " | " + message.State + "]\n" + message.Content));
                    _App?.Post(() =>
                    {
                        _Conversations = page.Items; int index = _Threads.SelectedIndex;
                        _Threads.SetItems(page.Items.Select(item => (item.Pinned ? "* " : "") + item.Title));
                        if (index >= 0 && index < page.Items.Count) _Threads.Select(index);
                        if (_LastContent != content) { _Transcript.Clear(); _Transcript.WriteLine(content.Length == 0 ? "Create a conversation with Ctrl+N, then send a note or start a debate with Ctrl+B." : content); _LastContent = content; }
                        _Status.RightText = "Connected | " + page.TotalCount + " conversations | Page " + _Page + " | Alt+Left/Right pages | Tab moves focus";
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException) { _App?.Post(() => _Status.RightText = "Connection: " + ex.Message); }
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
        private void Launch(Func<Task> action) { _ = ExecuteActionAsync(action); }
        private async Task ExecuteActionAsync(Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _App?.Post(() => _Status.RightText = ex.Message); }
        }
        private async Task ParticipantsAsync()
        {
            if (_App == null) return;
            List<RunnerProfile> profiles = await _Client.ParticipantsAsync(_Token).ConfigureAwait(false);
            string[] choices = new[] { "Create participant" }.Concat(profiles.Select(profile => profile.Name + " [" + profile.Kind + "/" + profile.Provider + "]" + (profile.Enabled ? "" : " disabled"))).ToArray();
            int selected = await UiAsync(() => _App.SelectAsync("Participants on the service machine", choices)).ConfigureAwait(false);
            if (selected < 0) return;
            RunnerProfile? profile = selected == 0 ? null : profiles[selected - 1];
            if (profile != null)
            {
                int action = await UiAsync(() => _App.SelectAsync(profile.Name, "Edit participant", "Delete reusable profile (history remains)")).ConfigureAwait(false);
                if (action < 0) return;
                if (action == 1)
                {
                    if (await UiAsync(() => _App.ConfirmAsync("Delete " + profile.Name + "? Historical run snapshots remain readable.")).ConfigureAwait(false))
                        await _Client.SendAsync<DeletionResponse>(HttpMethod.Delete, "v1.0/api/participants/" + Uri.EscapeDataString(profile.Id), token: _Token).ConfigureAwait(false);
                    return;
                }
            }
            RunnerProfile? updated = await UiAsync(async () =>
            {
                ParticipantEditor editor = new ParticipantEditor(profile);
                FormDialog<RunnerProfile> dialog = new FormDialog<RunnerProfile>(profile == null ? "Create participant" : "Edit participant", editor.Form, editor.Extract, _App, async candidate =>
                {
                    if (profile == null) await _Client.CreateParticipantAsync(candidate, _Token).ConfigureAwait(false);
                    else await _Client.SendAsync<RunnerProfile>(HttpMethod.Put, "v1.0/api/participants/" + Uri.EscapeDataString(profile.Id), candidate, _Token).ConfigureAwait(false);
                });
                dialog.ApplyTheme(_App.Theme);
                return await _App.ShowAsync<RunnerProfile>(dialog).ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (updated == null) return;
            _App.Post(() => _Status.RightText = "Participant saved. Existing run snapshots remain unchanged.");
        }
        private async Task NewAsync()
        {
            if (_App == null) return;
            string? title = await UiAsync(() => _App.PromptAsync("New conversation: enter the question to debate")).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(title)) return;
            Conversation conversation = await _Client.CreateConversationAsync(title, _Token).ConfigureAwait(false); _Selected = conversation.Id; _LastContent = "";
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
            if (_Selected == null || _App == null) return;
            List<RunnerProfile> profiles = await _Client.ParticipantsAsync(_Token).ConfigureAwait(false);
            if (profiles.Count < 2) throw new InvalidOperationException("Configure two participants with Ctrl+E first.");
            List<string> selected = new List<string>();
            int countChoice = await UiAsync(() => _App.SelectAsync("Debate participants", "Two speakers", "Three speakers")).ConfigureAwait(false);
            if (countChoice < 0) return;
            int count = countChoice + 2;
            if (profiles.Count(item => item.Enabled) < count) throw new InvalidOperationException("Enable enough participant profiles for this debate.");
            for (int turn = 0; turn < count; turn++)
            {
                List<RunnerProfile> choices = profiles.Where(item => !selected.Contains(item.Id) && item.Enabled).ToList();
                int index = await UiAsync(() => _App.SelectAsync("Choose speaker " + (turn + 1), choices.Select(item => item.Name + " (" + item.Kind + ")").ToArray())).ConfigureAwait(false);
                if (index < 0) return; selected.Add(choices[index].Id);
            }
            string? roundsText = await UiAsync(() => _App.PromptAsync("Maximum rounds (1 to 50)", "6")).ConfigureAwait(false);
            if (roundsText == null) return;
            string? secondsText = await UiAsync(() => _App.PromptAsync("Intervention window in seconds (0 to 600)", "15")).ConfigureAwait(false);
            if (secondsText == null) return;
            if (!int.TryParse(roundsText, out int rounds) || rounds < 1 || rounds > 50 || !int.TryParse(secondsText, out int seconds) || seconds < 0 || seconds > 600) throw new InvalidOperationException("Use rounds 1 to 50 and seconds 0 to 600.");
            await _Client.StartAsync(_Selected, new CreateRunRequest { ParticipantIds = selected, MaxRounds = rounds, InterventionSeconds = seconds, IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
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
            TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_App == null) throw new InvalidOperationException("Terminal is not running.");
            _App.Post(async () =>
            {
                try { completion.TrySetResult(await action().ConfigureAwait(false)); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        }
        private async Task SearchAsync()
        {
            if (_App == null) return;
            string? search = await UiAsync(() => _App.PromptAsync("Search conversation titles and message content", _Search)).ConfigureAwait(false);
            if (search == null) return;
            int archive = await UiAsync(() => _App.SelectAsync("Conversation scope", "Active conversations", "Archived conversations")).ConfigureAwait(false);
            if (archive < 0) return;
            _Search = search; _Archived = archive == 1; _Page = 1;
        }
        private async Task FollowupAsync()
        {
            if (_App == null || _Selected == null) return;
            string conversationId = _Selected;
            List<RunnerProfile> profiles = (await _Client.ParticipantsAsync(_Token).ConfigureAwait(false)).Where(profile => profile.Enabled).ToList();
            if (profiles.Count == 0) throw new InvalidOperationException("Configure an enabled participant first.");
            int index = await UiAsync(() => _App.SelectAsync("Choose follow-up respondent", profiles.Select(profile => profile.Name).ToArray())).ConfigureAwait(false);
            if (index < 0) return;
            string? question = await UiAsync(() => _App.PromptAsync("Follow-up question")).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(question)) return;
            await _Client.StartAsync(conversationId, new CreateRunRequest { Mode = "followup", Question = question, ParticipantIds = new List<string> { profiles[index].Id }, IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
        }
        private async Task ControlsAsync()
        {
            if (_App == null || _Selected == null) return;
            string conversationId = _Selected;
            DebateRun? run = (await _Client.RunsAsync(conversationId, _Token).ConfigureAwait(false)).LastOrDefault();
            if (run == null) throw new InvalidOperationException("This conversation has no runs.");
            string[] actions = new string[] { "pause", "resume", "interrupt", "continue", "extend", "steer", "stop" };
            int selected = await UiAsync(() => _App.SelectAsync("Run " + run.State + ": choose a command", "Pause after turn", "Resume interrupted run (retry may incur cost)", "Interrupt current generation", "Continue immediately", "Extend by two rounds", "Private directive to a participant", "Stop run")).ConfigureAwait(false);
            if (selected < 0) return;
            CommandRequest command = new CommandRequest { Action = actions[selected], Revision = run.Revision, IdempotencyKey = Guid.NewGuid().ToString("N") };
            if (command.Action == "steer")
            {
                int recipient = await UiAsync(() => _App.SelectAsync("Directive recipient (only this participant receives the text)", run.Participants.Select(profile => profile.Name).ToArray())).ConfigureAwait(false);
                if (recipient < 0) return;
                string? text = await UiAsync(() => _App.PromptAsync("Private directive for " + run.Participants[recipient].Name)).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text)) return;
                command.RecipientId = run.Participants[recipient].Id; command.Text = text;
            }
            if (command.Action == "stop" || command.Action == "interrupt")
            {
                bool accepted = await UiAsync(() => _App.ConfirmAsync(command.Action == "stop" ? "Stop this run? Partial answers remain in history." : "Interrupt the current answer? Partial text remains; resumption retries the turn.")).ConfigureAwait(false);
                if (!accepted) return;
            }
            await _Client.ActionAsync(conversationId, run.Id, command, _Token).ConfigureAwait(false);
        }
        private async Task HelpAsync()
        {
            if (_App != null) await UiAsync(() => _App.ConfirmAsync("Tab moves focus. Enter opens a selected conversation or sends a note. Ctrl+N creates a conversation; Ctrl+B selects two or three debaters; Ctrl+R opens all run controls; Ctrl+A asks a follow-up; Ctrl+F searches; Ctrl+E manages CLI/REST participants and literal arguments; Alt+Left/Right changes history pages; Ctrl+P pauses/resumes; Ctrl+T changes theme; Ctrl+Q detaches without stopping background work. The service continues running when you detach.", "Close", "Close")).ConfigureAwait(false);
        }
    }
}
