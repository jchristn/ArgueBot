namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
        private void Configure(TuiApplication app)
        {
            _App = app; app.Theme = Theme.Dark;
            app.AddWidget("threads", _Threads, region => region.LeftAnchored(0, 28).FillHeight(0, 4).WithBorder(BorderStyle.Line, "Conversations: Enter opens"));
            _Transcript = app.AddPane("transcript", region => region.FillWidth(28, 0).FillHeight(0, 4).WithBorder(BorderStyle.Line, "Shared debate history"));
            app.AddWidget("composer", _Composer, region => region.BottomAnchored(1, 3).FillWidth().WithBorder(BorderStyle.Line, "Note: Enter sends"));
            app.AddWidget("status", _Status, region => region.BottomAnchored(0, 1).FillWidth().WithPadding(0));
            app.BindKeyHints(_Status).AddAppHint("ctrl+n", "New conversation").AddAppHint("ctrl+b", "Start debate").AddAppHint("ctrl+p", "Pause/resume").AddAppHint("ctrl+t", "Theme").AddAppHint("f1", "Help").AddAppHint("ctrl+q", "Quit");
            app.Bind("ctrl+q", app.Quit);
            app.Bind("ctrl+n", () => Launch(NewAsync));
            app.Bind("ctrl+b", () => Launch(StartAsync));
            app.Bind("ctrl+p", () => Launch(PauseAsync));
            app.Bind("ctrl+t", () => app.Theme = ReferenceEquals(app.Theme, Theme.Dark) ? Theme.Light : Theme.Dark);
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
                    Page page = await _Client.ConversationsAsync(token: token).ConfigureAwait(false);
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
                        _Status.RightText = "Connected | " + page.TotalCount + " conversations | Tab moves focus";
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
        private async Task NewAsync()
        {
            if (_App == null) return;
            string? title = await _App.PromptAsync("New conversation: enter the question to debate").ConfigureAwait(false);
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
            if (profiles.Count < 2) throw new InvalidOperationException("Configure two participants using the console or web interface first.");
            List<string> selected = new List<string>();
            for (int turn = 0; turn < 2; turn++)
            {
                List<RunnerProfile> choices = profiles.Where(item => !selected.Contains(item.Id) && item.Enabled).ToList();
                int index = await _App.SelectAsync("Choose speaker " + (turn + 1), choices.Select(item => item.Name + " (" + item.Kind + ")").ToArray()).ConfigureAwait(false);
                if (index < 0) return; selected.Add(choices[index].Id);
            }
            await _Client.StartAsync(_Selected, new CreateRunRequest { ParticipantIds = selected, IdempotencyKey = Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
        }
        private async Task PauseAsync()
        {
            if (_Selected == null) return;
            List<DebateRun> runs = await _Client.RunsAsync(_Selected, _Token).ConfigureAwait(false);
            DebateRun? run = runs.LastOrDefault(); if (run == null) return;
            await _Client.ActionAsync(_Selected, run.Id, new CommandRequest { Action = run.State == "paused" ? "resume" : "pause", Revision = run.Revision }, _Token).ConfigureAwait(false);
        }
        private async Task HelpAsync()
        {
            if (_App != null) await _App.ConfirmAsync("Tab moves focus. Enter opens a selected conversation or sends a note. Ctrl+N creates a conversation; Ctrl+B selects two debaters; Ctrl+P pauses/resumes; Ctrl+T changes theme; Ctrl+Q detaches without stopping background work. This early replacement uses polling; full configuration and control screens are still being implemented.", "Close", "Close").ConfigureAwait(false);
        }
    }
}
