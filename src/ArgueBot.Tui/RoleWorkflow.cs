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

    internal sealed class RoleWorkflow
    {
        private readonly ArgueBotClient _Client;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;
        internal RoleWorkflow(ArgueBotClient client, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Application = application; _Dispatcher = dispatcher; _Token = token; }
        internal async Task RunAsync()
        {
            string? search = await _Dispatcher.InvokeAsync(() => _Application.PromptStyledAsync("Search protected built-ins and tenant custom role names; empty lists all")).ConfigureAwait(false);
            if (search == null) return;
            int page = 1;
            while (true)
            {
                RolePage result = await _Client.SendAsync<RolePage>(HttpMethod.Get, "v1.0/api/roles?search=" + Uri.EscapeDataString(search) + "&pageNumber=" + page, token: _Token).ConfigureAwait(false);
                List<string> choices = result.Items.Select(role => role.Name + " | " + (role.Builtin ? "built-in/protected" : "custom") + (role.Active ? " | enabled" : " | disabled")).ToList();
                int create = choices.Count; choices.Add("Create empty custom role");
                int previous = -1; int next = -1;
                if (page > 1) { previous = choices.Count; choices.Add("Previous page"); }
                if ((long)page * result.PageSize < result.TotalCount) { next = choices.Count; choices.Add("Next page"); }
                int refresh = choices.Count; choices.Add("Refresh roles");
                int selected = await _Dispatcher.InvokeAsync(() => _Application.SelectStyledAsync("Roles | page " + page + " | " + result.TotalCount + " total", choices.ToArray())).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == previous) { page--; continue; }
                if (selected == next) { page++; continue; }
                if (selected == refresh) continue;
                RoleRecord? role = selected == create ? null : result.Items[selected];
                bool clone = false;
                if (role != null)
                {
                    bool protectedRole = role.Protected || role.Builtin;
                    string[] actions = protectedRole ? new[] { "Clone into a tenant custom role", "Back" } : new[] { "Edit name or enabled state", "Clone into a tenant custom role", "Back" };
                    int action = await _Dispatcher.InvokeAsync(() => _Application.SelectStyledAsync(role.Name + (protectedRole ? " | protected: clone to customize" : " | custom role"), actions)).ConfigureAwait(false);
                    if (action < 0 || action == actions.Length - 1) continue;
                    clone = protectedRole || action == 1;
                }
                bool copy = clone;
                await _Dispatcher.InvokeAsync(async () =>
                {
                    RoleEditor editor = new RoleEditor(role, copy);
                    FormDialog<RoleRequest> dialog = new FormDialog<RoleRequest>(copy ? "Clone role: " + role!.Name : role == null ? "Create custom role" : "Edit custom role", editor.Form, editor.Extract, _Application, async request =>
                    {
                        bool update = role != null && !copy;
                        await _Client.SendAsync<RoleRecord>(update ? HttpMethod.Put : HttpMethod.Post, "v1.0/api/roles" + (update ? "/" + Uri.EscapeDataString(role!.Id) : ""), request, _Token).ConfigureAwait(false);
                    });
                    dialog.ApplyTheme(_Application.Theme);
                    return await _Application.ShowAsync<RoleRequest>(dialog).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }
    }
}
