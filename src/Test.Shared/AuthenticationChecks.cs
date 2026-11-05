namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>End-to-end checks of sign-in, bearer tokens, access classes, tenant isolation, rate limiting and audit against a running service.</summary>
    internal static class AuthenticationChecks
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        internal static async Task RunAsync(string endpoint, string home, CancellationToken token)
        {
            using (HttpClient http = new HttpClient(LocalServiceConnector.CreateHttpHandler(home), true) { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") })
            using (ArgueBotClient anonymous = LocalServiceConnector.CreateClient(endpoint, home, ""))
            using (ArgueBotClient admin = LocalServiceConnector.CreateClient(endpoint, home, ""))
            using (ArgueBotClient member = LocalServiceConnector.CreateClient(endpoint, home, ""))
            using (ArgueBotClient other = LocalServiceConnector.CreateClient(endpoint, home, ""))
            {
                HealthResponse health = await anonymous.SendAsync<HealthResponse>(HttpMethod.Get, "health", token: token).ConfigureAwait(false);
                Require(health.AuthenticationRequired, "Health must report that authentication is required.");
                LoginInfo info = await anonymous.LoginInfoAsync(token).ConfigureAwait(false);
                Require(info.DefaultEmail == IdentitySeed.Email && info.DefaultPassword == IdentitySeed.Password && info.DefaultToken == IdentitySeed.Token && !info.MultipleTenants, "Sign-in information must show the default credentials.");

                // Host allowlist: loopback and configured names only; browser origins must match the host.
                Uri root = new Uri(endpoint);
                await Status(http, HttpMethod.Get, "health", new Dictionary<string, string> { ["Host"] = "evil.example.test:" + root.Port }, 403, "invalid_host", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "health", new Dictionary<string, string> { ["Host"] = "arguebot.example.test:" + root.Port }, 200, "", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "health", new Dictionary<string, string> { ["Host"] = "localhost:" + root.Port }, 200, "", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "health", new Dictionary<string, string> { ["Origin"] = "https://evil.example.test" }, 403, "invalid_origin", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "health", new Dictionary<string, string> { ["Origin"] = root.GetLeftPart(UriPartial.Authority) }, 200, "", token).ConfigureAwait(false);

                // The allowlist type: loopback always, configured names, and * for any name; settings accept * and reject URLs.
                HostAllowlist named = new HostAllowlist(new[] { "arguebot.example.test", " " });
                Require(named.Allows("127.0.0.1") && named.Allows("LOCALHOST") && named.Allows("ArgueBot.Example.Test") && !named.Allows("other.example.test") && !named.Allows("") && !named.AllowsAny, "A listed allowlist must accept only loopback and its names.");
                HostAllowlist any = new HostAllowlist(new[] { HostAllowlist.Any });
                Require(any.AllowsAny && any.Allows("anything.example.test") && any.Allows("203.0.113.5") && !any.Allows(""), "* must accept any host name.");
                new ArgueBot.Core.Configuration.ServiceSettings { AllowedHosts = new List<string> { "*", "view.example.test" } }.Validate();
                try { new ArgueBot.Core.Configuration.ServiceSettings { AllowedHosts = new List<string> { "https://view.example.test/" } }.Validate(); throw new InvalidOperationException("A URL was accepted as a host name."); }
                catch (InvalidDataException) { }

                // Unauthenticated, malformed and conflicting carriers.
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", null, 401, "authentication_required", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v1.0/api/conversations", null, 401, "authentication_required", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Basic YWRtaW46cGFzc3dvcmQ=" }, 401, "unsupported_scheme", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer " + IdentitySeed.Token, ["x-token"] = "different" }, 401, "conflicting_credentials", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer not-a-token" }, 401, "invalid_token", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer abs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }, 401, "invalid_token", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["x-email"] = IdentitySeed.Email, ["x-password"] = IdentitySeed.Password }, 401, "password_not_accepted", token).ConfigureAwait(false);

                // The default bearer token works in both carriers, and the same token in both is accepted.
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer " + IdentitySeed.Token }, 200, "", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["x-token"] = IdentitySeed.Token }, 200, "", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer " + IdentitySeed.Token, ["x-token"] = IdentitySeed.Token }, 200, "", token).ConfigureAwait(false);

                // Password sign-in through the JSON body and through the header envelope.
                await Reject(() => anonymous.LoginAsync(IdentitySeed.Email, "wrong-password", "", token), 401, "invalid_credentials").ConfigureAwait(false);
                await Reject(() => anonymous.LoginAsync("nobody@example.test", "wrong-password", "", token), 401, "invalid_credentials").ConfigureAwait(false);
                TokenResponse signedIn = await admin.LoginAsync("ADMIN@arguebot", IdentitySeed.Password, "", token).ConfigureAwait(false);
                Require(signedIn.Token.StartsWith("abs_", StringComparison.Ordinal) && signedIn.User.Email == IdentitySeed.Email && signedIn.Tenant.Id == IdentitySeed.TenantId && signedIn.ExpiresUtc > DateTime.UtcNow.AddMinutes(55), "Sign-in must return a session token for the default administrator.");
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "v2.0/api/token"))
                {
                    request.Headers.Add("x-email", IdentitySeed.Email); request.Headers.Add("x-password", IdentitySeed.Password);
                    using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                        Require(response.StatusCode == HttpStatusCode.Created, "Header sign-in must issue a session token.");
                }
                TokenDetails details = await admin.TokenDetailsAsync(token).ConfigureAwait(false);
                Require(details.PrincipalType == "User" && details.SessionId == signedIn.SessionId && details.User.IsAdmin && details.ExpiresUtc.HasValue, "Token details must describe the session.");
                string session = signedIn.Token;
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["x-token"] = session }, 200, "", token).ConfigureAwait(false);
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer " + session, ["x-tenant-id"] = "ten_other" }, 403, "tenant_mismatch", token).ConfigureAwait(false);
                string tampered = session.Substring(0, session.Length - 2) + (session.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer " + tampered }, 401, "invalid_token", token).ConfigureAwait(false);

                // Refresh replaces the token; the previous token stops working.
                TokenResponse refreshed = await admin.RefreshAsync(token).ConfigureAwait(false);
                Require(refreshed.SessionId == signedIn.SessionId && refreshed.Token != session, "Refresh must issue a new token for the same session.");
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer " + session }, 401, "invalid_token", token).ConfigureAwait(false);
                await Reject(() => WithToken(anonymous, IdentitySeed.Token).RefreshAsync(token), 400, "not_refreshable").ConfigureAwait(false);
                anonymous.Token = "";

                // Members: data is shared within the tenant, administration is not.
                User created = await admin.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { FirstName = "Fixture", LastName = "Member", Email = "member@example.test", Password = "member-password" }, token).ConfigureAwait(false);
                await member.LoginAsync("member@example.test", "member-password", "", token).ConfigureAwait(false);
                Conversation shared = await admin.CreateConversationAsync("Shared in the tenant", token).ConfigureAwait(false);
                Page visible = await member.ConversationsAsync(token: token).ConfigureAwait(false);
                Require(visible.Items.Any(item => item.Id == shared.Id), "Members must see every conversation in their tenant.");
                Conversation renamed = await member.UpdateConversationAsync(new Conversation { Id = shared.Id, Title = "Renamed by a member", Revision = shared.Revision }, token).ConfigureAwait(false);
                Require(renamed.Title == "Renamed by a member", "Members must be able to change shared conversations.");
                await Reject(() => member.SendAsync<List<User>>(HttpMethod.Get, "v2.0/api/users", token: token), 403, "forbidden").ConfigureAwait(false);
                await Reject(() => member.SendAsync<List<Tenant>>(HttpMethod.Get, "v2.0/api/tenants", token: token), 403, "forbidden").ConfigureAwait(false);
                await Reject(() => member.SendAsync<object>(HttpMethod.Get, "v2.0/api/settings", token: token), 403, "forbidden").ConfigureAwait(false);
                await Reject(() => member.SendAsync<object>(HttpMethod.Get, "v2.0/api/request-history", token: token), 403, "forbidden").ConfigureAwait(false);
                await Reject(() => member.SendAsync<object>(HttpMethod.Get, "v2.0/api/configuration/secret-keys", token: token), 403, "forbidden").ConfigureAwait(false);
                User me = await member.SendAsync<User>(HttpMethod.Get, "v2.0/api/me", token: token).ConfigureAwait(false);
                Require(me.Id == created.Id && !me.IsTenantAdmin, "Members must read their own account.");

                // Own credentials: shown once, usable as a bearer token, revocable.
                AuthCredentialCreated credential = await member.SendAsync<AuthCredentialCreated>(HttpMethod.Post, "v2.0/api/me/credentials", new AuthCredentialRequest { Name = "Fixture automation" }, token).ConfigureAwait(false);
                Require(credential.Token.StartsWith("abt_", StringComparison.Ordinal) && credential.Credential.TokenLast4 == credential.Token.Substring(credential.Token.Length - 4), "A new credential must return its token once.");
                RevealedToken revealedOwn = await member.RevealTokenAsync(credential.Credential.Id, "", token).ConfigureAwait(false);
                Require(revealedOwn.Token == credential.Token, "A member must reveal their own token exactly.");
                Require((await admin.RevealTokenAsync(credential.Credential.Id, created.Id, token).ConfigureAwait(false)).Token == credential.Token, "A tenant administrator must reveal a member's token.");
                await Reject(() => member.RevealTokenAsync(IdentitySeed.CredentialId, IdentitySeed.UserId, token), 403, "forbidden").ConfigureAwait(false);
                Require((await admin.RevealTokenAsync(IdentitySeed.CredentialId, "", token).ConfigureAwait(false)).Token == IdentitySeed.Token, "The default token must be revealable.");
                using (ArgueBotClient automation = LocalServiceConnector.CreateClient(endpoint, home, credential.Token))
                {
                    TokenDetails automated = await automation.TokenDetailsAsync(token).ConfigureAwait(false);
                    Require(automated.PrincipalType == "Credential" && automated.CredentialId == credential.Credential.Id && automated.User.Id == created.Id, "A credential token must authenticate as its user.");
                    await member.SendAsync<AuthCredential>(HttpMethod.Put, "v2.0/api/me/credentials/" + credential.Credential.Id, new AuthCredentialRequest { Name = "Fixture automation", Active = false, Revision = credential.Credential.Revision }, token).ConfigureAwait(false);
                    await Reject(() => automation.TokenDetailsAsync(token), 401, "credential_inactive").ConfigureAwait(false);
                }
                List<AuthCredential> listed = await member.SendAsync<List<AuthCredential>>(HttpMethod.Get, "v2.0/api/me/credentials", token: token).ConfigureAwait(false);
                Require(listed.Count == 1 && !listed[0].Active, "Credential lists must show disabled credentials without their tokens.");

                // Sessions: list, revoke one, password change signs out the others.
                List<AuthSession> sessions = await member.SendAsync<List<AuthSession>>(HttpMethod.Get, "v2.0/api/me/sessions", token: token).ConfigureAwait(false);
                Require(sessions.Count == 1 && sessions[0].Current, "The caller's session must be listed as current.");
                using (ArgueBotClient second = LocalServiceConnector.CreateClient(endpoint, home, ""))
                {
                    await second.LoginAsync("member@example.test", "member-password", "", token).ConfigureAwait(false);
                    await member.SendAsync<Acknowledgement>(HttpMethod.Put, "v2.0/api/me/password", new PasswordChangeRequest { CurrentPassword = "member-password", NewPassword = "member-password-2" }, token).ConfigureAwait(false);
                    await Reject(() => second.TokenDetailsAsync(token), 401, "session_revoked").ConfigureAwait(false);
                    await member.TokenDetailsAsync(token).ConfigureAwait(false);
                }

                // Disabling the user ends their sessions immediately.
                User current = await admin.SendAsync<User>(HttpMethod.Get, "v2.0/api/users/" + created.Id, token: token).ConfigureAwait(false);
                await admin.SendAsync<User>(HttpMethod.Put, "v2.0/api/users/" + created.Id, new UserRequest { FirstName = current.FirstName, LastName = current.LastName, Email = current.Email, Active = false, Revision = current.Revision }, token).ConfigureAwait(false);
                await Reject(() => member.TokenDetailsAsync(token), 401, "session_revoked").ConfigureAwait(false);
                await Reject(() => member.LoginAsync("member@example.test", "member-password-2", "", token), 401, "account_inactive").ConfigureAwait(false);

                // Tenants: a second tenant's data stays invisible to the first.
                Tenant tenant = await admin.SendAsync<Tenant>(HttpMethod.Post, "v2.0/api/tenants", new TenantRequest { Name = "Fixture tenant" }, token).ConfigureAwait(false);
                User outsider;
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "v2.0/api/users") { Content = JsonContent.Create(new UserRequest { FirstName = "Other", LastName = "Admin", Email = "member@example.test", Password = "other-password", IsTenantAdmin = true }, options: Json) })
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token); request.Headers.Add("x-tenant-id", tenant.Id);
                    using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        Require(response.StatusCode == HttpStatusCode.Created, "A system administrator must create users in another tenant with x-tenant-id.");
                        outsider = (await response.Content.ReadFromJsonAsync<User>(Json, token).ConfigureAwait(false))!;
                    }
                }
                Require(outsider.TenantId == tenant.Id, "The user must belong to the selected tenant.");
                await other.LoginAsync("member@example.test", "other-password", "", token).ConfigureAwait(false);
                Page otherPage = await other.ConversationsAsync(token: token).ConfigureAwait(false);
                Require(otherPage.Items.All(item => item.Id != shared.Id), "Another tenant must not see this tenant's conversations.");
                await Reject(() => other.ConversationAsync(shared.Id, token), 404, "").ConfigureAwait(false);
                Conversation theirs = await other.CreateConversationAsync("Only in the fixture tenant", token).ConfigureAwait(false);
                Require((await admin.ConversationsAsync(token: token).ConfigureAwait(false)).Items.All(item => item.Id != theirs.Id), "The default tenant must not see another tenant's conversations.");
                Require((await anonymous.LoginInfoAsync(token).ConfigureAwait(false)).MultipleTenants, "Sign-in information must report several tenants.");

                // Every resource type is tenant-scoped: another tenant can neither list nor reach this tenant's records.
                CredentialMetadata secret = await admin.SaveCredentialAsync("", new CredentialRequest { Name = "Fixture secret", Kind = "token", SecretValue = "fictional-tenant-secret" }, token).ConfigureAwait(false);
                EndpointProfile endpointProfile = await admin.SendAsync<EndpointProfile>(HttpMethod.Post, "v2.0/api/configuration/endpoints", new EndpointProfile { Name = "Fixture endpoint", Protocol = "ollama", BaseUrl = "http://127.0.0.1:9" }, token).ConfigureAwait(false);
                PromptTemplate factory = (await admin.PromptsAsync(token: token).ConfigureAwait(false)).First();
                PromptTemplate prompt = await admin.DuplicatePromptAsync(factory.Id, token).ConfigureAwait(false);
                Require((await other.CredentialsAsync(token).ConfigureAwait(false)).All(item => item.Id != secret.Id), "Another tenant must not list this tenant's secrets.");
                await Reject(() => other.RevealCredentialAsync(secret.Id, token), 404, "").ConfigureAwait(false);
                Require((await other.EndpointsAsync(token).ConfigureAwait(false)).All(item => item.Id != endpointProfile.Id), "Another tenant must not list this tenant's endpoints.");
                await Reject(() => other.PreviewEndpointAsync(endpointProfile.Id, token), 404, "").ConfigureAwait(false);
                Require((await other.PromptsAsync(token: token).ConfigureAwait(false)).All(item => item.Id != prompt.Id), "Another tenant must not list this tenant's prompts.");
                await Reject(() => other.PromptAsync(prompt.Id, token), 404, "").ConfigureAwait(false);
                await Reject(() => other.SendAsync<object>(HttpMethod.Get, "v2.0/api/conversations/" + shared.Id + "/runs", token: token), 404, "").ConfigureAwait(false);
                await Reject(() => other.SendAsync<object>(HttpMethod.Get, "v2.0/api/conversations/" + shared.Id + "/messages", token: token), 404, "").ConfigureAwait(false);
                await Reject(() => other.UpdateConversationAsync(new Conversation { Id = shared.Id, Title = "Hijacked", Revision = renamed.Revision }, token), 404, "").ConfigureAwait(false);
                await Reject(() => other.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/conversations/" + shared.Id, token: token), 404, "").ConfigureAwait(false);

                // Public links: shown once, readable without a token, limited to the transcript, scoped to the tenant and revocable.
                await admin.NoteAsync(shared.Id, "A moderator note that a reader may see.", null, token).ConfigureAwait(false);
                await Reject(() => admin.SendAsync<ConversationShareCreated>(HttpMethod.Post, "v2.0/api/conversations/" + shared.Id + "/shares", new ConversationShareRequest { ExpiresInHours = 5000 }, token), 400, "invalid_expiry").ConfigureAwait(false);
                ConversationShareCreated link = await admin.SendAsync<ConversationShareCreated>(HttpMethod.Post, "v2.0/api/conversations/" + shared.Id + "/shares", new ConversationShareRequest { ExpiresInHours = 1 }, token).ConfigureAwait(false);
                Require(link.Path == "/share/" + link.Token && link.Url.EndsWith(link.Path, StringComparison.Ordinal) && link.Url.StartsWith("https://", StringComparison.Ordinal) && link.Share.ExpiresUtc <= DateTime.UtcNow.AddHours(1).AddMinutes(1), "A new link must return its token, path and URL once.");
                using (HttpRequestMessage proxied = new HttpRequestMessage(HttpMethod.Post, "v2.0/api/conversations/" + shared.Id + "/shares") { Content = JsonContent.Create(new ConversationShareRequest { ExpiresInHours = 1 }, options: Json) })
                {
                    proxied.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
                    proxied.Headers.Add("X-Forwarded-Host", "public.example.test");
                    proxied.Headers.Add("X-Forwarded-Proto", "https");
                    using (HttpResponseMessage response = await http.SendAsync(proxied, token).ConfigureAwait(false))
                    {
                        ConversationShareCreated forwarded = (await response.Content.ReadFromJsonAsync<ConversationShareCreated>(Json, token).ConfigureAwait(false))!;
                        Require(forwarded.Url.StartsWith("https://public.example.test/share/", StringComparison.Ordinal), "Share links behind a proxy must use X-Forwarded-Host: " + forwarded.Url);
                        await admin.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/conversations/" + shared.Id + "/shares/" + forwarded.Share.Id, token: token).ConfigureAwait(false);
                    }
                }
                SharedTranscript transcript = await anonymous.SendAsync<SharedTranscript>(HttpMethod.Get, "v2.0/api/shares/" + link.Token, token: token).ConfigureAwait(false);
                Require(transcript.Title == "Renamed by a member" && transcript.Messages.Any(item => item.Content.Contains("moderator note", StringComparison.Ordinal)) && !transcript.IncludeToolCalls, "A public link must return the transcript without sign-in.");
                using (HttpResponseMessage page = await http.GetAsync("share/" + link.Token, token).ConfigureAwait(false))
                    // 503 means this test build has no dashboard; the page is still public and still sends no referrer.
                    Require((page.IsSuccessStatusCode || page.StatusCode == HttpStatusCode.ServiceUnavailable) && page.Headers.TryGetValues("Referrer-Policy", out IEnumerable<string>? policy) && policy.Contains("no-referrer"), "The share page must load without sign-in and never send its token as a referrer.");
                ConversationShareCreated shownAgain = await admin.RevealShareAsync(shared.Id, link.Share.Id, token).ConfigureAwait(false);
                Require(shownAgain.Url == link.Url, "Revealing a share link must return the same URL.");
                await Reject(() => other.RevealShareAsync(shared.Id, link.Share.Id, token), 404, "").ConfigureAwait(false);
                List<ConversationShare> links = await admin.SendAsync<List<ConversationShare>>(HttpMethod.Get, "v2.0/api/conversations/" + shared.Id + "/shares", token: token).ConfigureAwait(false);
                Require(links.Count == 2 && links.Single(item => item.Id == link.Share.Id).ViewCount == 1 && links.Count(item => item.Active) == 1 && !JsonSerializer.Serialize(links).Contains(link.Token, StringComparison.Ordinal), "Link lists must count views and never include the token.");
                Require((await admin.SendAsync<AuditPage>(HttpMethod.Get, "v2.0/api/audit?operation=ShareRevealed", token: token).ConfigureAwait(false)).TotalCount >= 1, "Share reveals must be audited.");
                await Reject(() => other.SendAsync<List<ConversationShare>>(HttpMethod.Get, "v2.0/api/conversations/" + shared.Id + "/shares", token: token), 404, "").ConfigureAwait(false);
                await Reject(() => other.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/conversations/" + shared.Id + "/shares/" + link.Share.Id, token: token), 404, "").ConfigureAwait(false);
                HistoryPage captured = await admin.SendAsync<HistoryPage>(HttpMethod.Get, "v2.0/api/request-history?search=" + Uri.EscapeDataString("/v2.0/api/shares/"), token: token).ConfigureAwait(false);
                Require(captured.Items.Count > 0 && captured.Items.All(item => !item.Path.Contains(link.Token, StringComparison.Ordinal)), "Request history must not store share tokens.");
                await admin.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/conversations/" + shared.Id + "/shares/" + link.Share.Id, token: token).ConfigureAwait(false);
                await Reject(() => anonymous.SendAsync<SharedTranscript>(HttpMethod.Get, "v2.0/api/shares/" + link.Token, token: token), 404, "share_not_found").ConfigureAwait(false);
                await Reject(() => anonymous.SendAsync<SharedTranscript>(HttpMethod.Get, "v2.0/api/shares/" + new string('A', 43), token: token), 404, "share_not_found").ConfigureAwait(false);
                Require((await admin.ConversationAsync(shared.Id, token).ConfigureAwait(false)).Title == "Renamed by a member", "A cross-tenant write must not change the conversation.");

                // Disabling a tenant ends its sessions; deleting it needs its exact name and removes its data.
                Tenant fixture = await admin.SendAsync<Tenant>(HttpMethod.Get, "v2.0/api/tenants/" + tenant.Id, token: token).ConfigureAwait(false);
                await admin.SendAsync<Tenant>(HttpMethod.Put, "v2.0/api/tenants/" + tenant.Id, new TenantRequest { Name = fixture.Name, Active = false, Revision = fixture.Revision }, token).ConfigureAwait(false);
                await Reject(() => other.TokenDetailsAsync(token), 401, "tenant_inactive").ConfigureAwait(false);
                await Reject(() => admin.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/tenants/" + tenant.Id + "?confirmName=wrong", token: token), 400, "confirmation_mismatch").ConfigureAwait(false);
                await admin.SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/tenants/" + tenant.Id + "?confirmName=" + Uri.EscapeDataString(fixture.Name), token: token).ConfigureAwait(false);
                await Reject(() => admin.SendAsync<Tenant>(HttpMethod.Get, "v2.0/api/tenants/" + tenant.Id, token: token), 404, "tenant_not_found").ConfigureAwait(false);

                // Secrets are reachable under the 2.1 name and the deprecated alias.
                await admin.SendAsync<List<CredentialMetadata>>(HttpMethod.Get, "v2.0/api/configuration/secrets", token: token).ConfigureAwait(false);
                await admin.SendAsync<List<CredentialMetadata>>(HttpMethod.Get, "v2.0/api/configuration/credentials", token: token).ConfigureAwait(false);

                // The audit log holds failures, denials and administrative changes, and never a password.
                AuditPage failures = await admin.SendAsync<AuditPage>(HttpMethod.Get, "v2.0/api/audit?operation=AuthFailure&pageSize=200", token: token).ConfigureAwait(false);
                Require(failures.TotalCount >= 3 && failures.Items.Any(item => item.ErrorCode == "invalid_credentials") && failures.Items.Any(item => item.ErrorCode == "authentication_required"), "Sign-in and token failures must be audited.");
                AuditPage denials = await admin.SendAsync<AuditPage>(HttpMethod.Get, "v2.0/api/audit?operation=AuthorizationDenied&pageSize=200", token: token).ConfigureAwait(false);
                Require(denials.Items.Any(item => item.Path == "/v2.0/api/users" && item.UserId == created.Id && item.StatusCode == 403), "Authorization denials must be audited with the path and user.");
                AuditPage all = await admin.SendAsync<AuditPage>(HttpMethod.Get, "v2.0/api/audit?pageSize=200", token: token).ConfigureAwait(false);
                Require(all.Items.Any(item => item.Operation == "UserCreated") && all.Items.Any(item => item.Operation == "TenantDeleted") && all.Items.Any(item => item.Operation == "AuthorizationBypass") && all.Items.Any(item => item.Operation == "SessionIssued"), "Administrative changes and bypasses must be audited.");
                Require(!JsonSerializer.Serialize(all.Items).Contains("member-password", StringComparison.Ordinal) && !JsonSerializer.Serialize(all.Items).Contains(credential.Token, StringComparison.Ordinal), "Audit records must never hold passwords or tokens.");

                // Contract drift: every documented operation exists, declares the access class the gate enforces, and refuses a member when it needs more.
                await admin.SendAsync<User>(HttpMethod.Post, "v2.0/api/users", new UserRequest { FirstName = "Contract", LastName = "Member", Email = "contract@example.test", Password = "contract-password" }, token).ConfigureAwait(false);
                using (ArgueBotClient contractMember = LocalServiceConnector.CreateClient(endpoint, home, ""))
                using (HttpResponseMessage document = await http.GetAsync("openapi.json", token).ConfigureAwait(false))
                {
                    Require(document.IsSuccessStatusCode, "The OpenAPI document must be public.");
                    await contractMember.LoginAsync("contract@example.test", "contract-password", "", token).ConfigureAwait(false);
                    using (JsonDocument spec = JsonDocument.Parse(await document.Content.ReadAsStringAsync(token).ConfigureAwait(false)))
                    {
                        int checkedOperations = 0;
                        foreach (JsonProperty route in spec.RootElement.GetProperty("paths").EnumerateObject())
                        {
                            foreach (JsonProperty operation in route.Value.EnumerateObject())
                            {
                                string method = operation.Name.ToUpperInvariant();
                                string access = operation.Value.GetProperty("x-arguebot-access").GetString() ?? "";
                                bool isPublic = operation.Value.TryGetProperty("security", out JsonElement security) && security.GetArrayLength() == 0;
                                Require(isPublic == (access == RouteAccess.Public), method + " " + route.Name + " must have no security requirement exactly when it is public.");
                                if (!isPublic) Require(RouteAccess.Classify(method, route.Name) == access, method + " " + route.Name + " is documented as " + access + " but the gate enforces " + RouteAccess.Classify(method, route.Name) + ".");
                                if (route.Name.Contains("/settings/restart", StringComparison.Ordinal) || route.Name.Contains("/settings/shutdown", StringComparison.Ordinal) || (method == "DELETE" && route.Name == "/v2.0/api/token")) continue;
                                string path = System.Text.RegularExpressions.Regex.Replace(route.Name, "{[A-Za-z]+}", "missing_fixture");
                                if (access == RouteAccess.TenantAdmin || access == RouteAccess.SystemAdmin)
                                {
                                    using (HttpRequestMessage denied = new HttpRequestMessage(new HttpMethod(method), path.TrimStart('/')))
                                    {
                                        denied.Headers.Authorization = new AuthenticationHeaderValue("Bearer", contractMember.Token);
                                        using (HttpResponseMessage response = await http.SendAsync(denied, token).ConfigureAwait(false))
                                            Require(response.StatusCode == HttpStatusCode.Forbidden && (await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Contains("\"requiredPermission\":\"" + access + "\"", StringComparison.Ordinal), method + " " + route.Name + " must refuse a member with 403 naming " + access + ".");
                                    }
                                }
                                if (method != "GET") { checkedOperations++; continue; }
                                using (HttpRequestMessage probe = new HttpRequestMessage(HttpMethod.Get, path.TrimStart('/')))
                                {
                                    probe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IdentitySeed.Token);
                                    using (HttpResponseMessage response = await http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                                    {
                                        string body = response.Content.Headers.ContentType?.MediaType == "application/json" ? await response.Content.ReadAsStringAsync(token).ConfigureAwait(false) : "";
                                        Require(!body.Contains("Route not found.", StringComparison.Ordinal), "GET " + route.Name + " is documented but not served.");
                                    }
                                }
                                checkedOperations++;
                            }
                        }
                        Require(checkedOperations > 100, "The OpenAPI document must list every operation.");
                    }
                }

                // Sign out revokes the session.
                await admin.LogoutAsync(token).ConfigureAwait(false);
                admin.Token = refreshed.Token;
                await Reject(() => admin.TokenDetailsAsync(token), 401, "session_revoked").ConfigureAwait(false);

                // Rate limiting: ten failures from one address in five minutes block further sign-in attempts from it.
                // Earlier failures in this check count toward the same address, so the block may start before the tenth new attempt.
                int rejected = 0;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try { await anonymous.LoginAsync("limited-" + attempt + "@example.test", "wrong", "", token).ConfigureAwait(false); throw new InvalidDataException("A wrong password was accepted."); }
                    catch (DomainException ex) when (ex.StatusCode == 401 && ex.Code == "invalid_credentials") { rejected++; }
                    catch (DomainException ex) when (ex.StatusCode == 429 && rejected >= 5) { break; }
                }
                using (HttpResponseMessage limited = await http.PostAsJsonAsync("v2.0/api/token", new LoginRequest { Email = IdentitySeed.Email, Password = IdentitySeed.Password }, Json, token).ConfigureAwait(false))
                    Require(limited.StatusCode == (HttpStatusCode)429 && limited.Headers.RetryAfter != null, "Sign-in must be rate limited with Retry-After after repeated failures.");
                await Status(http, HttpMethod.Get, "v2.0/api/conversations", new Dictionary<string, string> { ["Authorization"] = "Bearer " + IdentitySeed.Token }, 200, "", token).ConfigureAwait(false);
            }
        }

        private static ArgueBotClient WithToken(ArgueBotClient client, string value) { client.Token = value; return client; }

        private static async Task Status(HttpClient http, HttpMethod method, string path, Dictionary<string, string>? headers, int status, string code, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (headers != null) foreach (KeyValuePair<string, string> header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if ((int)response.StatusCode != status) throw new InvalidDataException(method + " " + path + " returned " + (int)response.StatusCode + " instead of " + status + ": " + body);
                    if (code.Length > 0 && !body.Contains("\"code\":\"" + code + "\"", StringComparison.Ordinal)) throw new InvalidDataException(method + " " + path + " returned " + body + " instead of code " + code);
                }
            }
        }
        private static async Task Reject(Func<Task> action, int status, string code)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) when (ex.StatusCode == status && (code.Length == 0 || ex.Code == code)) { return; }
            catch (DomainException ex) { throw new InvalidDataException("Expected " + status + " " + code + " but got " + ex.StatusCode + " " + ex.Code + ": " + ex.Message); }
            throw new InvalidDataException("Expected " + status + " " + code + " but the request succeeded.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
