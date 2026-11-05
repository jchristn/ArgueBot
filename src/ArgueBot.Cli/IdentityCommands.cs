namespace ArgueBot.Cli
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
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Sign-in, the caller's account and tokens, administration (users, tenants, audit) and public share links.</summary>
    internal static class IdentityCommands
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        internal static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal) { "login", "logout", "whoami", "me", "credentials", "tokens", "sessions", "users", "tenants", "audit", "shares" };

        internal static async Task<int> RunAsync(ArgueBotClient client, List<string> words, string server, string home, bool allowHttp, CancellationToken token)
        {
            string command = words[0]; string operation = words.Count > 1 ? words[1] : "list";
            switch (command)
            {
                case "login": return await LoginAsync(client, words, server, home, allowHttp, token).ConfigureAwait(false);
                case "logout":
                    {
                        ClientSession? saved = ClientSessionStore.Load(home);
                        if (saved != null && ClientSessionStore.SameServer(saved.Server, server) && saved.Token.StartsWith(SessionProtector.Prefix, StringComparison.Ordinal))
                        {
                            client.Token = saved.Token;
                            try { await client.LogoutAsync(token).ConfigureAwait(false); } catch (DomainException ex) when (ex.StatusCode == 401) { }
                        }
                        ClientSessionStore.Clear(home);
                        Console.WriteLine("Signed out.");
                        return 0;
                    }
                case "whoami": Program.Print(await client.TokenDetailsAsync(token).ConfigureAwait(false)); return 0;
                case "me":
                    if (operation == "show" || operation == "list") { Program.Print(await client.SendAsync<User>(HttpMethod.Get, "v2.0/api/me", token: token).ConfigureAwait(false)); return 0; }
                    if (operation == "password")
                    {
                        string current = ReadSecret("Current password: ");
                        string next = ReadSecret("New password (8 or more characters): ");
                        if (!Console.IsInputRedirected && ReadSecret("Repeat the new password: ") != next) throw new ArgumentException("The new passwords do not match.");
                        Program.Print(await client.SendAsync<Acknowledgement>(HttpMethod.Put, "v2.0/api/me/password", new PasswordChangeRequest { CurrentPassword = current, NewPassword = next }, token).ConfigureAwait(false));
                        return 0;
                    }
                    throw new ArgumentException("Use me show or me password.");
                case "credentials":
                case "tokens":
                    {
                        string user = Option(words, "--user") ?? "";
                        string root = user.Length == 0 ? "v2.0/api/me/credentials" : "v2.0/api/users/" + Uri.EscapeDataString(user) + "/credentials";
                        switch (operation)
                        {
                            case "list": Program.Print(await client.SendAsync<List<AuthCredential>>(HttpMethod.Get, root, token: token).ConfigureAwait(false)); return 0;
                            case "create":
                                {
                                    Program.Require(words, 3);
                                    string? days = Option(words, "--days");
                                    DateTime? expires = days == null ? null : DateTime.UtcNow.AddDays(int.Parse(days, CultureInfo.InvariantCulture));
                                    AuthCredentialCreated created = await client.SendAsync<AuthCredentialCreated>(HttpMethod.Post, root, new AuthCredentialRequest { Name = words[2], ExpiresUtc = expires }, token).ConfigureAwait(false);
                                    Program.Print(created);
                                    Console.Error.WriteLine("Store this token like a password. 'credentials reveal ID' shows it again (audited).");
                                    return 0;
                                }
                            case "disable":
                            case "enable":
                                {
                                    Program.Require(words, 3);
                                    AuthCredential current = (await client.SendAsync<List<AuthCredential>>(HttpMethod.Get, root, token: token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == words[2]) ?? throw new DomainException(404, "credential_not_found", "No credential has this ID.");
                                    Program.Print(await client.SendAsync<AuthCredential>(HttpMethod.Put, root + "/" + Uri.EscapeDataString(current.Id), new AuthCredentialRequest { Name = current.Name, ExpiresUtc = current.ExpiresUtc, Active = operation == "enable", Revision = current.Revision }, token).ConfigureAwait(false));
                                    return 0;
                                }
                            case "reveal": Program.Require(words, 3); Program.Print(await client.RevealTokenAsync(words[2], user, token).ConfigureAwait(false)); return 0;
                            case "delete": Program.Require(words, 3); Program.Print(await client.SendAsync<Acknowledgement>(HttpMethod.Delete, root + "/" + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false)); return 0;
                            default: throw new ArgumentException("Use credentials list|create NAME [--days N]|reveal ID|disable ID|enable ID|delete ID, with --user ID for another user.");
                        }
                    }
                case "sessions":
                    {
                        string? user = Option(words, "--user");
                        if (operation == "list") { Program.Print(await client.SendAsync<List<AuthSession>>(HttpMethod.Get, user == null ? "v2.0/api/me/sessions" : "v2.0/api/users/" + Uri.EscapeDataString(user) + "/sessions", token: token).ConfigureAwait(false)); return 0; }
                        if (operation == "revoke") { Program.Require(words, 3); Program.Print(await client.SendAsync<Acknowledgement>(HttpMethod.Delete, (user == null ? "v2.0/api/me/sessions/" : "v2.0/api/sessions/") + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false)); return 0; }
                        throw new ArgumentException("Use sessions list|revoke ID, with --user ID for another user.");
                    }
                case "users":
                    switch (operation)
                    {
                        case "list": Program.Print(await client.SendAsync<List<User>>(HttpMethod.Get, "v2.0/api/users", token: token).ConfigureAwait(false)); return 0;
                        case "show": Program.Require(words, 3); Program.Print(await client.SendAsync<User>(HttpMethod.Get, "v2.0/api/users/" + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false)); return 0;
                        case "add":
                            {
                                Program.Require(words, 3);
                                UserRequest request = JsonSerializer.Deserialize<UserRequest>(await Program.TextAsync(words, 2, token).ConfigureAwait(false), Json) ?? throw new ArgumentException("Provide a user JSON object.");
                                if (request.Password.Length == 0 && !Console.IsInputRedirected) request.Password = ReadSecret("Password for the new user: ");
                                Program.Print(await client.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", request, token).ConfigureAwait(false));
                                return 0;
                            }
                        case "update":
                            {
                                Program.Require(words, 4);
                                UserRequest request = JsonSerializer.Deserialize<UserRequest>(await Program.TextAsync(words, 3, token).ConfigureAwait(false), Json) ?? throw new ArgumentException("Provide a user JSON object with its revision.");
                                Program.Print(await client.SendAsync<User>(HttpMethod.Put, "v2.0/api/users/" + Uri.EscapeDataString(words[2]), request, token).ConfigureAwait(false));
                                return 0;
                            }
                        case "password":
                            Program.Require(words, 3);
                            Program.Print(await client.SendAsync<Acknowledgement>(HttpMethod.Put, "v2.0/api/users/" + Uri.EscapeDataString(words[2]) + "/password", new PasswordChangeRequest { NewPassword = ReadSecret("New password (8 or more characters): ") }, token).ConfigureAwait(false));
                            return 0;
                        case "delete": Program.Require(words, 3); Program.Print(await client.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/users/" + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false)); return 0;
                        default: throw new ArgumentException("Use users list|show ID|add JSON|-|--file PATH|update ID JSON|-|--file PATH|password ID|delete ID. Add --tenant ID (system administrators) to work in another tenant.");
                    }
                case "tenants":
                    switch (operation)
                    {
                        case "list": Program.Print(await client.SendAsync<List<Tenant>>(HttpMethod.Get, "v2.0/api/tenants", token: token).ConfigureAwait(false)); return 0;
                        case "add": Program.Require(words, 3); Program.Print(await client.SendAsync<Tenant>(HttpMethod.Post, "v2.0/api/tenants", new TenantRequest { Name = string.Join(" ", words.Skip(2)) }, token).ConfigureAwait(false)); return 0;
                        case "rename":
                        case "enable":
                        case "disable":
                            {
                                Program.Require(words, operation == "rename" ? 4 : 3);
                                Tenant current = await client.SendAsync<Tenant>(HttpMethod.Get, "v2.0/api/tenants/" + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false);
                                TenantRequest request = new TenantRequest { Name = operation == "rename" ? string.Join(" ", words.Skip(3)) : current.Name, Active = operation == "rename" ? current.Active : operation == "enable", Revision = current.Revision };
                                Program.Print(await client.SendAsync<Tenant>(HttpMethod.Put, "v2.0/api/tenants/" + Uri.EscapeDataString(current.Id), request, token).ConfigureAwait(false));
                                return 0;
                            }
                        case "delete": Program.Require(words, 4); Program.Print(await client.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/tenants/" + Uri.EscapeDataString(words[2]) + "?confirmName=" + Uri.EscapeDataString(string.Join(" ", words.Skip(3))), token: token).ConfigureAwait(false)); return 0;
                        default: throw new ArgumentException("Use tenants list|add NAME|rename ID NAME|enable ID|disable ID|delete ID NAME.");
                    }
                case "audit":
                    {
                        List<string> query = new List<string>();
                        foreach (string name in new[] { "operation", "outcome", "userId", "fromUtc", "toUtc", "pageNumber", "pageSize" })
                        {
                            string? value = Option(words, "--" + name);
                            if (value != null) query.Add(name + "=" + Uri.EscapeDataString(value));
                        }
                        Program.Print(await client.SendAsync<AuditPage>(HttpMethod.Get, "v2.0/api/audit" + (query.Count == 0 ? "" : "?" + string.Join("&", query)), token: token).ConfigureAwait(false));
                        return 0;
                    }
                case "shares":
                    {
                        Program.Require(words, 3);
                        string root = "v2.0/api/conversations/" + Uri.EscapeDataString(words[2]) + "/shares";
                        switch (operation)
                        {
                            case "list": Program.Print(await client.SendAsync<List<ConversationShare>>(HttpMethod.Get, root, token: token).ConfigureAwait(false)); return 0;
                            case "create":
                                {
                                    string? hours = Option(words, "--hours");
                                    ConversationShareCreated created = await client.SendAsync<ConversationShareCreated>(HttpMethod.Post, root, new ConversationShareRequest { ExpiresInHours = hours == null ? 168 : int.Parse(hours, CultureInfo.InvariantCulture), IncludeToolCalls = words.Contains("--tools") }, token).ConfigureAwait(false);
                                    Program.Print(created);
                                    Console.Error.WriteLine("Anyone with this link can read the transcript until " + created.Share.ExpiresUtc.ToString("u", CultureInfo.InvariantCulture) + ". 'shares reveal' shows it again while it is active.");
                                    return 0;
                                }
                            case "reveal": Program.Require(words, 4); Program.Print(await client.RevealShareAsync(words[2], words[3], token).ConfigureAwait(false)); return 0;
                            case "revoke": Program.Require(words, 4); Program.Print(await client.SendAsync<Acknowledgement>(HttpMethod.Delete, root + "/" + Uri.EscapeDataString(words[3]), token: token).ConfigureAwait(false)); return 0;
                            default: throw new ArgumentException("Use shares list CONVERSATION|create CONVERSATION [--hours N] [--tools]|reveal CONVERSATION SHARE_ID|revoke CONVERSATION SHARE_ID.");
                        }
                    }
                default: throw new ArgumentException("Unknown command.");
            }
        }

        private static async Task<int> LoginAsync(ArgueBotClient client, List<string> words, string server, string home, bool allowHttp, CancellationToken token)
        {
            string? bearer = Option(words, "--token");
            client.Token = "";
            if (bearer != null)
            {
                client.Token = bearer;
                TokenDetails details = await client.TokenDetailsAsync(token).ConfigureAwait(false);
                ClientSessionStore.Save(home, new ClientSession { Server = server, Token = bearer, ExpiresUtc = details.ExpiresUtc, Email = details.User.Email, TenantName = details.Tenant.Name, AllowInsecureHttp = allowHttp });
                Program.Print(details);
                return 0;
            }
            ClientSession? saved = ClientSessionStore.Load(home);
            string email = Option(words, "--email") ?? (Console.IsInputRedirected ? (Console.ReadLine() ?? "") : Prompt("Email" + (saved?.Email.Length > 0 ? " [" + saved.Email + "]" : "") + ": ", saved?.Email ?? ""));
            string password = words.Contains("--password-stdin") || Console.IsInputRedirected ? (Console.ReadLine() ?? "") : ReadSecret("Password: ");
            string tenant = Option(words, "--tenant") ?? "";
            TokenResponse response;
            try { response = await client.LoginAsync(email.Trim(), password, tenant, token).ConfigureAwait(false); }
            catch (TenantRequiredException ex)
            {
                if (Console.IsInputRedirected) throw new ArgumentException("This email belongs to several tenants. Sign in again with --tenant ID: " + string.Join(", ", ex.Tenants.Select(item => item.Id + " (" + item.Name + ")")) + ".");
                Console.Error.WriteLine("This email belongs to several tenants:");
                for (int index = 0; index < ex.Tenants.Count; index++) Console.Error.WriteLine("  " + (index + 1) + ". " + ex.Tenants[index].Name + " (" + ex.Tenants[index].Id + ")");
                string choice = Prompt("Tenant number: ", "");
                if (!int.TryParse(choice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 1 || number > ex.Tenants.Count) throw new ArgumentException("Choose a listed tenant number.");
                response = await client.LoginAsync(email.Trim(), password, ex.Tenants[number - 1].Id, token).ConfigureAwait(false);
            }
            ClientSessionStore.Save(home, new ClientSession { Server = server, Token = response.Token, ExpiresUtc = response.ExpiresUtc, Email = response.User.Email, TenantName = response.Tenant.Name, AllowInsecureHttp = allowHttp });
            Program.Print(new { signedIn = true, server, user = response.User, tenant = response.Tenant, expiresUtc = response.ExpiresUtc, sessionId = response.SessionId });
            Console.Error.WriteLine("Signed in. The session is remembered for this home and refreshes while you use it.");
            return 0;
        }

        /// <summary>Use a remembered session for this server, refreshing it when it has less than ten minutes left.</summary>
        internal static async Task UseRememberedAsync(ArgueBotClient client, string server, string home, CancellationToken token)
        {
            ClientSession? saved = ClientSessionStore.Load(home);
            if (saved == null || !ClientSessionStore.SameServer(saved.Server, server)) return;
            if (saved.ExpiresUtc.HasValue && saved.ExpiresUtc.Value <= DateTime.UtcNow) return;
            client.Token = saved.Token;
            if (saved.ExpiresUtc.HasValue && saved.ExpiresUtc.Value - DateTime.UtcNow < TimeSpan.FromMinutes(10))
            {
                try
                {
                    TokenResponse refreshed = await client.RefreshAsync(token).ConfigureAwait(false);
                    saved.Token = refreshed.Token; saved.ExpiresUtc = refreshed.ExpiresUtc;
                    ClientSessionStore.Save(home, saved);
                }
                catch (DomainException ex) when (ex.StatusCode == 401) { client.Token = ""; }
            }
        }

        internal static string? Option(List<string> words, string name)
        {
            int index = words.IndexOf(name);
            if (index < 0) return null;
            if (index + 1 >= words.Count) throw new ArgumentException(name + " needs a value.");
            string value = words[index + 1]; words.RemoveRange(index, 2);
            return value;
        }

        private static string Prompt(string label, string fallback)
        {
            Console.Error.Write(label);
            string value = Console.ReadLine() ?? "";
            return value.Length == 0 ? fallback : value;
        }

        /// <summary>Read a secret without echoing it; from a redirected stdin, read one line.</summary>
        internal static string ReadSecret(string label)
        {
            if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
            Console.Error.Write(label);
            StringBuilder value = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; continue; }
                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) throw new OperationCanceledException();
                if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
            }
            Console.Error.WriteLine();
            return value.ToString();
        }
    }
}
