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

    internal sealed class AttemptWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;
        internal AttemptWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }
        internal async Task RunAsync(string conversationId)
        {
            List<DebateRun> runs = await _Client.RunsAsync(conversationId, _Token).ConfigureAwait(false); runs.Reverse();
            if (runs.Count == 0) throw new InvalidOperationException("This conversation has no runs yet.");
            int selectedRun = await _Dispatcher.InvokeAsync(() => _Application.SelectStyledAsync("Choose current or historical run", runs.Select(run => run.Id + " | " + run.State + " | " + run.Outcome).ToArray())).ConfigureAwait(false);
            if (selectedRun < 0) return;
            DebateRun run = runs[selectedRun]; int page = 1;
            while (true)
            {
                AttemptPage result = await _Client.AttemptsAsync(conversationId, run.Id, page, token: _Token).ConfigureAwait(false);
                List<string> choices = result.Items.Select(attempt => attempt.Number + " | " + attempt.State + " | " + attempt.Id).ToList();
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if ((long)page * result.PageSize < result.TotalCount) { next = choices.Count; choices.Add("Next page"); }
                int refresh = choices.Count; choices.Add("Refresh generation attempts");
                int selected = await _Dispatcher.InvokeAsync(() => _Application.SelectStyledAsync("Attempts | page " + page + " | " + result.TotalCount + " total", choices.ToArray())).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                if (selected == refresh) continue;
                GenerationAttempt attempt = result.Items[selected];
                await _Dispatcher.InvokeAsync(async () =>
                {
                    AttemptDetails details = new AttemptDetails(attempt, run);
                    return await _Application.ShowAsync<GenerationAttempt>(new FormDialog<GenerationAttempt>("Generation provenance", details.Form, () => attempt, readOnly: true)).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }
    }
}
