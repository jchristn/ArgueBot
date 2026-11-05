namespace ArgueBot.Tui
{
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TUIKit.Hosting;

    /// <summary>Account, administration, audit and share-link actions for the terminal workspace.</summary>
    internal sealed class AccountWorkflow
    {
        private static readonly string[] Lifetimes = { "1 hour", "1 day", "7 days", "30 days", "90 days" };
        private static readonly int[] LifetimeHours = { 1, 24, 168, 720, 2160 };
        private readonly ArgueBotClient _Client;
        private readonly TuiSession _Session;
        private readonly TuiApplication _Application;
        private readonly UiDispatcher _Dispatcher;
        private readonly CancellationToken _Token;
        internal AccountWorkflow(ArgueBotClient client, TuiSession session, TuiApplication application, UiDispatcher dispatcher, CancellationToken token)
        { _Client = client; _Session = session; _Application = application; _Dispatcher = dispatcher; _Token = token; }

        private Task<T> Ui<T>(Func<Task<T>> action) { return _Dispatcher.InvokeAsync(action); }
        private Task<int> SelectAsync(string title, IEnumerable<string> choices) { return Ui(() => _Application.SelectStyledAsync(title, choices.ToArray())); }
        private Task<string?> PromptAsync(string title, string initial = "") { return Ui(() => _Application.PromptStyledAsync(title, initial)); }
        private Task<string?> SecretAsync(string title) { return Ui(() => _Application.PromptSecretAsync(title)); }
        private Task<bool> ConfirmAsync(string message, string confirm) { return Ui(() => _Application.ConfirmStyledAsync(message, confirm, "Cancel")); }
        private async Task PanelAsync(string title, string content)
        {
            await Ui(async () =>
            {
                MessageDialog dialog = new MessageDialog(new Message { Speaker = "ArgueBot", Kind = title, Content = content }) { Title = title };
                dialog.ApplyTheme(_Application.Theme);
                await _Application.ShowAsync(dialog).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        private static string Date(DateTime? value) { return value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "never"; }
        private static string Name(User user) { string name = (user.FirstName + " " + user.LastName).Trim(); return name.Length == 0 ? user.Email : name + " <" + user.Email + ">"; }
        private static string Role(User user) { return user.IsAdmin ? "system administrator" : user.IsTenantAdmin ? "tenant administrator" : "member"; }

        internal async Task ShowAccountAsync()
        {
            User me = _Session.Details.User;
            await PanelAsync("Signed in", "User: " + Name(me) + "\n\nRole: " + Role(me) + "\n\nTenant: " + _Session.Details.Tenant.Name + "\n\nServer: " + _Session.Server + "\n\nSigned in with: " + (_Session.IsSession ? "email and password (session ends " + Date(_Session.ExpiresUtc) + " unless the TUI refreshes it)" : "a bearer token")).ConfigureAwait(false);
        }

        internal async Task ChangePasswordAsync()
        {
            string? current = await SecretAsync("Current password").ConfigureAwait(false); if (current == null) return;
            string? next = await SecretAsync("New password (8 or more characters)").ConfigureAwait(false); if (next == null) return;
            string? repeat = await SecretAsync("Repeat the new password").ConfigureAwait(false); if (repeat == null) return;
            if (repeat != next) throw new InvalidOperationException("The new passwords do not match.");
            await _Client.SendAsync<Acknowledgement>(HttpMethod.Put, "v2.0/api/me/password", new PasswordChangeRequest { CurrentPassword = current, NewPassword = next }, _Token).ConfigureAwait(false);
            await PanelAsync("Password changed", "Your password is changed. Your other sessions were signed out; this one continues.").ConfigureAwait(false);
        }

        internal Task TokensAsync() { return TokensAsync(_Client, "v2.0/api/me/credentials", "Your bearer tokens"); }

        private async Task TokensAsync(ArgueBotClient client, string root, string title)
        {
            while (true)
            {
                List<AuthCredential> tokens = await client.SendAsync<List<AuthCredential>>(HttpMethod.Get, root, token: _Token).ConfigureAwait(false);
                List<string> choices = new List<string> { "Create a token" };
                choices.AddRange(tokens.Select(item => item.Name + "  ****" + item.TokenLast4 + "  " + (item.Active ? "active" : "disabled") + ", expires " + Date(item.ExpiresUtc) + ", last used " + Date(item.LastUsedUtc) + (item.Protected ? ", default" : "")));
                int selected = await SelectAsync(title, choices).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == 0)
                {
                    string? name = await PromptAsync("Name for the token, for example CI or laptop").ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    int expiry = await SelectAsync("When should it expire?", new[] { "Never", "In 30 days", "In 90 days", "In one year" }).ConfigureAwait(false);
                    if (expiry < 0) continue;
                    DateTime? expires = expiry == 0 ? null : DateTime.UtcNow.AddDays(new[] { 0, 30, 90, 365 }[expiry]);
                    AuthCredentialCreated created = await client.SendAsync<AuthCredentialCreated>(HttpMethod.Post, root, new AuthCredentialRequest { Name = name.Trim(), ExpiresUtc = expires }, _Token).ConfigureAwait(false);
                    await PanelAsync("Copy your new token", "This is the only time the token is shown. Copy it now and store it like a password.\n\n" + created.Token + "\n\nUse it as Authorization: Bearer, with x-token, with the CLI's --token, or as ARGUEBOT_TOKEN.").ConfigureAwait(false);
                    continue;
                }
                AuthCredential chosen = tokens[selected - 1];
                if (chosen.Protected) { await PanelAsync(chosen.Name, "The default administrator token cannot be disabled or deleted. Create your own tokens and stop using it.").ConfigureAwait(false); continue; }
                int action = await SelectAsync(chosen.Name, new[] { chosen.Active ? "Disable" : "Enable", "Delete" }).ConfigureAwait(false);
                if (action == 0) await client.SendAsync<AuthCredential>(HttpMethod.Put, root + "/" + Uri.EscapeDataString(chosen.Id), new AuthCredentialRequest { Name = chosen.Name, ExpiresUtc = chosen.ExpiresUtc, Active = !chosen.Active, Revision = chosen.Revision }, _Token).ConfigureAwait(false);
                else if (action == 1 && await ConfirmAsync("Delete " + chosen.Name + "? Anything using it stops working now.", "Delete").ConfigureAwait(false)) await client.SendAsync<Acknowledgement>(HttpMethod.Delete, root + "/" + Uri.EscapeDataString(chosen.Id), token: _Token).ConfigureAwait(false);
            }
        }

        internal async Task SessionsAsync()
        {
            if (!_Session.IsSession) { await PanelAsync("Sessions", "You signed in with a bearer token, which has no session. Sign in with your email and password to manage sessions.").ConfigureAwait(false); return; }
            while (true)
            {
                List<AuthSession> sessions = await _Client.SendAsync<List<AuthSession>>(HttpMethod.Get, "v2.0/api/me/sessions", token: _Token).ConfigureAwait(false);
                int selected = await SelectAsync("Your sessions", sessions.Select(item => (item.Current ? "This session: " : "") + "signed in " + Date(item.CreatedUtc) + " from " + item.SourceIp + ", last used " + Date(item.LastUsedUtc))).ConfigureAwait(false);
                if (selected < 0) return;
                if (sessions[selected].Current) { await PanelAsync("This session", "Use Sign out in the Menu to end this session.").ConfigureAwait(false); continue; }
                if (await ConfirmAsync("Sign out that session?", "Sign out").ConfigureAwait(false)) await _Client.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/me/sessions/" + Uri.EscapeDataString(sessions[selected].Id), token: _Token).ConfigureAwait(false);
            }
        }

        internal Task UsersAsync() { return UsersAsync(_Client, _Session.Details.Tenant.Name); }

        private async Task UsersAsync(ArgueBotClient client, string tenantName)
        {
            while (true)
            {
                List<User> users = await client.SendAsync<List<User>>(HttpMethod.Get, "v2.0/api/users", token: _Token).ConfigureAwait(false);
                List<string> choices = new List<string> { "Add a user" };
                choices.AddRange(users.Select(user => Name(user) + "  " + Role(user) + (user.Active ? "" : ", disabled") + (user.Protected ? ", default" : "")));
                int selected = await SelectAsync("Users in " + tenantName, choices).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == 0) { await AddUserAsync(client).ConfigureAwait(false); continue; }
                await EditUserAsync(client, users[selected - 1]).ConfigureAwait(false);
            }
        }

        private async Task AddUserAsync(ArgueBotClient client)
        {
            string? email = await PromptAsync("Email").ConfigureAwait(false); if (string.IsNullOrWhiteSpace(email)) return;
            string? first = await PromptAsync("First name").ConfigureAwait(false); if (first == null) return;
            string? last = await PromptAsync("Last name").ConfigureAwait(false); if (last == null) return;
            string? password = await SecretAsync("Password (8 or more characters; the user can change it later)").ConfigureAwait(false); if (password == null) return;
            List<string> roles = new List<string> { "Member: uses the tenant's conversations and configuration", "Tenant administrator: also manages this tenant's users and audit log" };
            if (_Session.IsSystemAdmin) roles.Add("System administrator: manages every tenant");
            int role = await SelectAsync("Role for " + email.Trim(), roles).ConfigureAwait(false); if (role < 0) return;
            User created = await client.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { Email = email.Trim(), FirstName = first.Trim(), LastName = last.Trim(), Password = password, IsTenantAdmin = role >= 1, IsAdmin = role == 2 }, _Token).ConfigureAwait(false);
            await PanelAsync("User added", Name(created) + " can now sign in as a " + Role(created) + ".").ConfigureAwait(false);
        }

        private async Task EditUserAsync(ArgueBotClient client, User user)
        {
            List<string> actions = new List<string> { "Change name and email", "Reset password", user.Active ? "Disable sign-in" : "Enable sign-in" };
            if (!user.IsAdmin) actions.Add(user.IsTenantAdmin ? "Make a member" : "Make a tenant administrator");
            if (_Session.IsSystemAdmin) actions.Add(user.IsAdmin ? "Remove system administration" : "Make a system administrator");
            actions.Add("Bearer tokens");
            actions.Add("Delete");
            int selected = await SelectAsync(Name(user) + " (" + Role(user) + ")", actions).ConfigureAwait(false);
            if (selected < 0) return;
            string action = actions[selected];
            UserRequest request = new UserRequest { FirstName = user.FirstName, LastName = user.LastName, Email = user.Email, IsAdmin = user.IsAdmin, IsTenantAdmin = user.IsTenantAdmin, Active = user.Active, Revision = user.Revision };
            string root = "v2.0/api/users/" + Uri.EscapeDataString(user.Id);
            switch (action)
            {
                case "Change name and email":
                    {
                        string? first = await PromptAsync("First name", user.FirstName).ConfigureAwait(false); if (first == null) return;
                        string? last = await PromptAsync("Last name", user.LastName).ConfigureAwait(false); if (last == null) return;
                        string? email = await PromptAsync("Email", user.Email).ConfigureAwait(false); if (email == null) return;
                        request.FirstName = first.Trim(); request.LastName = last.Trim(); request.Email = email.Trim();
                        break;
                    }
                case "Reset password":
                    {
                        string? password = await SecretAsync("New password for " + user.Email).ConfigureAwait(false); if (password == null) return;
                        await client.SendAsync<Acknowledgement>(HttpMethod.Put, root + "/password", new PasswordChangeRequest { NewPassword = password }, _Token).ConfigureAwait(false);
                        await PanelAsync("Password reset", "The password for " + user.Email + " is reset and their sessions are signed out.").ConfigureAwait(false);
                        return;
                    }
                case "Bearer tokens": await TokensAsync(client, root + "/credentials", "Bearer tokens for " + user.Email).ConfigureAwait(false); return;
                case "Delete":
                    if (await ConfirmAsync("Delete " + user.Email + "? Their conversations stay in the tenant; their tokens and sessions stop working.", "Delete").ConfigureAwait(false))
                        await client.SendAsync<Acknowledgement>(HttpMethod.Delete, root, token: _Token).ConfigureAwait(false);
                    return;
                case "Make a member": request.IsTenantAdmin = false; break;
                case "Make a tenant administrator": request.IsTenantAdmin = true; break;
                case "Make a system administrator": request.IsAdmin = true; request.IsTenantAdmin = true; break;
                case "Remove system administration": request.IsAdmin = false; break;
                default: request.Active = !user.Active; break;
            }
            await client.SendAsync<User>(HttpMethod.Put, root, request, _Token).ConfigureAwait(false);
        }

        internal async Task TenantsAsync()
        {
            while (true)
            {
                List<Tenant> tenants = await _Client.SendAsync<List<Tenant>>(HttpMethod.Get, "v2.0/api/tenants", token: _Token).ConfigureAwait(false);
                List<string> choices = new List<string> { "Add a tenant" };
                choices.AddRange(tenants.Select(item => item.Name + (item.Id == _Session.Details.Tenant.Id ? "  (yours)" : "") + (item.Active ? "" : "  disabled") + (item.Protected ? "  default" : "")));
                int selected = await SelectAsync("Tenants", choices).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == 0)
                {
                    string? name = await PromptAsync("Tenant name").ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    Tenant created = await _Client.SendAsync<Tenant>(HttpMethod.Post, "v2.0/api/tenants", new TenantRequest { Name = name.Trim() }, _Token).ConfigureAwait(false);
                    await PanelAsync("Tenant added", created.Name + " is ready. Open it and add its first tenant administrator under Users.").ConfigureAwait(false);
                    continue;
                }
                Tenant tenant = tenants[selected - 1];
                List<string> actions = new List<string> { "Users", "Rename" };
                if (!tenant.Protected) { actions.Add(tenant.Active ? "Disable" : "Enable"); if (tenant.Id != _Session.Details.Tenant.Id) actions.Add("Delete"); }
                int action = await SelectAsync(tenant.Name, actions).ConfigureAwait(false);
                if (action < 0) continue;
                string root = "v2.0/api/tenants/" + Uri.EscapeDataString(tenant.Id);
                switch (actions[action])
                {
                    case "Users":
                        if (tenant.Id == _Session.Details.Tenant.Id) { await UsersAsync().ConfigureAwait(false); break; }
                        using (ArgueBotClient scoped = LocalServiceConnector.CreateClient(_Session.Server, _Session.Home, _Session.Token, _Session.AllowInsecureHttp))
                        {
                            scoped.TenantId = tenant.Id;
                            await UsersAsync(scoped, tenant.Name).ConfigureAwait(false);
                        }
                        break;
                    case "Rename":
                        {
                            string? name = await PromptAsync("New name", tenant.Name).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(name)) await _Client.SendAsync<Tenant>(HttpMethod.Put, root, new TenantRequest { Name = name.Trim(), Active = tenant.Active, Revision = tenant.Revision }, _Token).ConfigureAwait(false);
                            break;
                        }
                    case "Delete":
                        {
                            string? typed = await PromptAsync("Type " + tenant.Name + " to delete it and everything in it").ConfigureAwait(false);
                            if (typed == null) break;
                            await _Client.SendAsync<Acknowledgement>(HttpMethod.Delete, root + "?confirmName=" + Uri.EscapeDataString(typed), token: _Token).ConfigureAwait(false);
                            break;
                        }
                    default: await _Client.SendAsync<Tenant>(HttpMethod.Put, root, new TenantRequest { Name = tenant.Name, Active = !tenant.Active, Revision = tenant.Revision }, _Token).ConfigureAwait(false); break;
                }
            }
        }

        internal async Task AuditAsync()
        {
            AuditPage page = await _Client.SendAsync<AuditPage>(HttpMethod.Get, "v2.0/api/audit?pageSize=50", token: _Token).ConfigureAwait(false);
            StringBuilder text = new StringBuilder("The 50 newest of " + page.TotalCount + " audit records. The dashboard's Audit log page filters and pages through all of them.\n\n");
            foreach (AuditEvent item in page.Items)
                text.Append(Date(item.CreatedUtc)).Append("  ").Append(item.Operation).Append("  ").Append(item.Outcome).Append(item.StatusCode > 0 ? " " + item.StatusCode : "").Append('\n')
                    .Append("    ").Append(item.UserId.Length == 0 ? "not signed in" : item.UserId).Append(item.Method.Length > 0 ? "  " + item.Method + " " + item.Path : "").Append(item.SourceIp.Length > 0 ? "  from " + item.SourceIp : "").Append('\n')
                    .Append(item.Reason.Length > 0 ? "    " + item.Reason + "\n" : "").Append('\n');
            await PanelAsync("Audit log", text.ToString()).ConfigureAwait(false);
        }

        internal async Task ShareAsync(string conversationId, string title)
        {
            string root = "v2.0/api/conversations/" + Uri.EscapeDataString(conversationId) + "/shares";
            while (true)
            {
                List<ConversationShare> shares = await _Client.SendAsync<List<ConversationShare>>(HttpMethod.Get, root, token: _Token).ConfigureAwait(false);
                List<string> choices = new List<string> { "Create a read-only link" };
                choices.AddRange(shares.Select(item => "Link ..." + item.TokenLast4 + "  " + (item.RevokedUtc != null ? "revoked" : item.Active ? "active until " + Date(item.ExpiresUtc) : "expired") + ", " + item.ViewCount + (item.ViewCount == 1 ? " view" : " views") + (item.IncludeToolCalls ? ", with tool calls" : "")));
                int selected = await SelectAsync("Share " + title, choices).ConfigureAwait(false);
                if (selected < 0) return;
                if (selected == 0)
                {
                    int lifetime = await SelectAsync("The link stops working after", Lifetimes).ConfigureAwait(false); if (lifetime < 0) continue;
                    int tools = await SelectAsync("Include agents' tool calls and results?", new[] { "No: only the debate turns", "Yes: also tool calls, such as files read" }).ConfigureAwait(false); if (tools < 0) continue;
                    ConversationShareCreated created = await _Client.SendAsync<ConversationShareCreated>(HttpMethod.Post, root, new ConversationShareRequest { ExpiresInHours = LifetimeHours[lifetime], IncludeToolCalls = tools == 1 }, _Token).ConfigureAwait(false);
                    await PanelAsync("Your read-only link", "Anyone with this link can read the transcript until " + Date(created.Share.ExpiresUtc) + ". It is shown only now.\n\n" + created.Url + "\n\nRevoke it here at any time.").ConfigureAwait(false);
                    continue;
                }
                ConversationShare share = shares[selected - 1];
                if (!share.Active) continue;
                if (await ConfirmAsync("Revoke link ..." + share.TokenLast4 + "? It stops working now.", "Revoke").ConfigureAwait(false)) await _Client.SendAsync<Acknowledgement>(HttpMethod.Delete, root + "/" + Uri.EscapeDataString(share.Id), token: _Token).ConfigureAwait(false);
            }
        }
    }
}
