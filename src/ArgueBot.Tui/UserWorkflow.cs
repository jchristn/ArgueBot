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

    internal sealed class UserWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;
        internal UserWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }
        internal async Task RunAsync()
        {
            RequestContext principal = await _Client.SendAsync<RequestContext>(HttpMethod.Get, "v1.0/api/auth/me", token: _Token).ConfigureAwait(false);
            string? search = await _Dispatcher.InvokeAsync(() => _Application.PromptStyledAsync("Search tenant user names/email; empty lists all")).ConfigureAwait(false);
            if (search == null) return;
            int page = 1;
            while (true)
            {
                UserPage result = await _Client.SendAsync<UserPage>(HttpMethod.Get, "v1.0/api/users?search=" + Uri.EscapeDataString(search) + "&pageNumber=" + page, token: _Token).ConfigureAwait(false);
                List<string> choices = result.Items.Select(user => user.Name + " | " + user.Email + " | " + (user.Active ? user.Role : "disabled") + (user.Protected ? " | protected" : "")).ToList();
                int create = choices.Count; choices.Add("Create tenant user");
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if ((long)page * result.PageSize < result.TotalCount) { next = choices.Count; choices.Add("Next page"); }
                int refresh = choices.Count; choices.Add("Refresh identities");
                int selected = await _Dispatcher.InvokeAsync(() => _Application.SelectStyledAsync("Tenant users | page " + page + " | " + result.TotalCount + " total", choices.ToArray())).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                if (selected == refresh) continue;
                UserRecord? user = selected == create ? null : result.Items[selected];
                if (user != null && (user.Protected || (user.IsAdmin && !principal.IsAdmin)))
                {
                    await _Dispatcher.InvokeAsync(() => _Application.ConfirmStyledAsync("This identity is protected or requires a system administrator. No changes are allowed.", "Close", "Close")).ConfigureAwait(false); continue;
                }
                await _Dispatcher.InvokeAsync(async () =>
                {
                    UserEditor editor = new UserEditor(principal.IsAdmin, user);
                    FormDialog<UserRequest> dialog = new FormDialog<UserRequest>(user == null ? "Create tenant user" : "Edit tenant user", editor.Form, editor.Extract, _Application, async request =>
                    {
                        await _Client.SendAsync<UserRecord>(user == null ? HttpMethod.Post : HttpMethod.Put, "v1.0/api/users" + (user == null ? "" : "/" + Uri.EscapeDataString(user.Id)), request, _Token).ConfigureAwait(false);
                    });
                    dialog.ApplyTheme(_Application.Theme);
                    return await _Application.ShowAsync<UserRequest>(dialog).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }
    }
}
