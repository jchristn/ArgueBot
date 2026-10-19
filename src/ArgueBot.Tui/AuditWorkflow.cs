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
    using TUIKit.Hosting;

    internal sealed class AuditWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;
        internal AuditWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }
        internal async Task RunAsync()
        {
            string? operation = await _Dispatcher.InvokeAsync(() => _Application.PromptStyledAsync("Exact security operation code, or empty for all events")).ConfigureAwait(false);
            if (operation == null) return;
            int page = 1;
            while (true)
            {
                AuditPage result = await _Client.SendAsync<AuditPage>(HttpMethod.Get, "v1.0/api/audit?operation=" + Uri.EscapeDataString(operation) + "&pageNumber=" + page, token: _Token).ConfigureAwait(false);
                List<string> choices = result.Items.Select(entry => entry.Operation + " | " + entry.Outcome + " | " + (entry.UserId ?? "anonymous/system")).ToList();
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if ((long)page * result.PageSize < result.TotalCount) { next = choices.Count; choices.Add("Next page"); }
                int refresh = choices.Count; choices.Add("Refresh security events");
                int selected = await _Dispatcher.InvokeAsync(() => _Application.SelectStyledAsync("Security audit | page " + page + " | " + result.TotalCount + " total", choices.ToArray())).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                if (selected == refresh) continue;
                AuditEntry entry = result.Items[selected];
                await _Dispatcher.InvokeAsync(async () =>
                {
                    AuditDetails details = new AuditDetails(entry);
                    FormDialog<AuditEntry> dialog = new FormDialog<AuditEntry>("Immutable security event", details.Form, () => entry, readOnly: true);
                    return await _Application.ShowAsync<AuditEntry>(dialog).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }
    }
}
