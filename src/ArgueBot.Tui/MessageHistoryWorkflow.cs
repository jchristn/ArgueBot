namespace ArgueBot.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using TUIKit.Hosting;

    internal sealed class MessageHistoryWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;
        internal MessageHistoryWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }
        internal async Task RunAsync(string conversationId)
        {
            int page = 1; long? through = null; bool tail = true;
            while (true)
            {
                MessagePage result = await _Client.MessagePageAsync(conversationId, page, throughSequence: through, tail: tail, token: _Token).ConfigureAwait(false);
                page = result.PageNumber; through = result.ThroughSequence; tail = false;
                List<string> choices = result.Items.Select(message => TranscriptFormatter.Heading(message) + (message.ContentTruncated ? " (preview)" : "")).ToList();
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if ((long)page * result.PageSize < result.TotalCount) { next = choices.Count; choices.Add("Next page"); }
                int latest = choices.Count; choices.Add(result.LatestSequence > result.ThroughSequence ? "Newer messages available: show latest" : "Refresh latest messages");
                int selected = await _Dispatcher.InvokeAsync(() => _Application.SelectAsync("Message history | page " + page + " | " + result.TotalCount + " total", choices.ToArray())).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                if (selected == latest) { through = null; tail = true; continue; }
                Message full = await _Client.MessageAsync(conversationId, result.Items[selected].Id, _Token).ConfigureAwait(false);
                await _Dispatcher.InvokeAsync(() => _Application.ShowAsync<Message>(new MessageDialog(full))).ConfigureAwait(false);
            }
        }
    }
}
