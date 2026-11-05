namespace ArgueBot.Server.Routes
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>The caller's account, users and credentials (tenant administrators), tenants (system administrators) and the audit log.</summary>
    internal sealed class IdentityRoutes
    {
        private const string Root = "/v2.0/api";
        private readonly DatabaseDriverBase _Database;
        private readonly RequestAuthenticator _Authenticator;
        internal IdentityRoutes(DatabaseDriverBase database, RequestAuthenticator authenticator) { _Database = database; _Authenticator = authenticator; }

        internal void Register(Webserver server)
        {
            // The caller's own account.
            Static(server, HttpMethod.GET, "/me", "Read the signed-in user", "Account", async http => await Api.SendAsync(http, await _Database.Identity.UserAsync(Api.Context(http), Api.Context(http).UserId, http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Static(server, HttpMethod.PUT, "/me", "Change your name or email with the expected revision", "Account", async http =>
            {
                RequestContext context = Api.Context(http); UserRequest body = Api.Body<UserRequest>(http);
                User current = await _Database.Identity.UserAsync(context, context.UserId, http.Token).ConfigureAwait(false);
                User updated = await _Database.Identity.UpdateUserAsync(context, context.UserId, new UserRequest { FirstName = body.FirstName, LastName = body.LastName, Email = body.Email.Length == 0 ? current.Email : body.Email, IsAdmin = current.IsAdmin, IsTenantAdmin = current.IsTenantAdmin, Active = current.Active, Revision = body.Revision }, http.Token).ConfigureAwait(false);
                await AuditAsync(http, "UserUpdated", "user", updated.Id, "Updated own profile").ConfigureAwait(false);
                await Api.SendAsync(http, updated).ConfigureAwait(false);
            });
            Static(server, HttpMethod.PUT, "/me/password", "Change your password; signs out your other sessions", "Account", async http =>
            {
                RequestContext context = Api.Context(http);
                await _Database.Identity.SetPasswordAsync(context, context.UserId, Api.Body<PasswordChangeRequest>(http), http.Token).ConfigureAwait(false);
                await AuditAsync(http, "PasswordChanged", "user", context.UserId, "Changed own password").ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            });
            Static(server, HttpMethod.GET, "/me/credentials", "List your bearer-token credentials", "Account", async http => await Api.SendAsync(http, await _Database.Identity.CredentialsAsync(Api.Context(http), Api.Context(http).UserId, http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Static(server, HttpMethod.POST, "/me/credentials", "Create a bearer-token credential; the token is returned only once", "Account", http => CreateCredentialAsync(http, Api.Context(http).UserId));
            Parameter(server, HttpMethod.PUT, "/me/credentials/{credentialId}", "Rename, enable or disable one of your credentials", "Account", http => UpdateCredentialAsync(http, Api.Context(http).UserId));
            Parameter(server, HttpMethod.DELETE, "/me/credentials/{credentialId}", "Delete one of your credentials", "Account", http => DeleteCredentialAsync(http, Api.Context(http).UserId));
            Static(server, HttpMethod.GET, "/me/sessions", "List your active sessions", "Account", async http => await Api.SendAsync(http, await _Database.Identity.SessionsAsync(Api.Context(http), Api.Context(http).UserId, http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Parameter(server, HttpMethod.DELETE, "/me/sessions/{sessionId}", "Sign out one of your sessions", "Account", http => RevokeSessionAsync(http));

            // Users and their credentials and sessions: tenant administrators.
            Static(server, HttpMethod.GET, "/users", "List users in your tenant", "Users", async http => await Api.SendAsync(http, await _Database.Identity.UsersAsync(Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Static(server, HttpMethod.POST, "/users", "Create a user in your tenant", "Users", async http =>
            {
                User created = await _Database.Identity.CreateUserAsync(Api.Context(http), Api.Body<UserRequest>(http), http.Token).ConfigureAwait(false);
                await AuditAsync(http, "UserCreated", "user", created.Id, created.Email).ConfigureAwait(false);
                await Api.SendAsync(http, created, 201).ConfigureAwait(false);
            });
            Parameter(server, HttpMethod.GET, "/users/{userId}", "Read a user", "Users", async http => await Api.SendAsync(http, await _Database.Identity.UserAsync(Api.Context(http), Api.Parameter(http, "userId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Parameter(server, HttpMethod.PUT, "/users/{userId}", "Update a user with the expected revision", "Users", async http =>
            {
                User updated = await _Database.Identity.UpdateUserAsync(Api.Context(http), Api.Parameter(http, "userId"), Api.Body<UserRequest>(http), http.Token).ConfigureAwait(false);
                await AuditAsync(http, "UserUpdated", "user", updated.Id, (updated.Active ? "active" : "disabled") + (updated.IsAdmin ? ", system administrator" : updated.IsTenantAdmin ? ", tenant administrator" : "")).ConfigureAwait(false);
                await Api.SendAsync(http, updated).ConfigureAwait(false);
            });
            Parameter(server, HttpMethod.DELETE, "/users/{userId}", "Delete a user; their conversations and agents stay in the tenant", "Users", async http =>
            {
                string id = Api.Parameter(http, "userId");
                await _Database.Identity.DeleteUserAsync(Api.Context(http), id, http.Token).ConfigureAwait(false);
                await AuditAsync(http, "UserDeleted", "user", id, "").ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            });
            Parameter(server, HttpMethod.PUT, "/users/{userId}/password", "Reset a user's password; signs out their sessions", "Users", async http =>
            {
                string id = Api.Parameter(http, "userId");
                await _Database.Identity.SetPasswordAsync(Api.Context(http), id, Api.Body<PasswordChangeRequest>(http), http.Token).ConfigureAwait(false);
                await AuditAsync(http, "PasswordChanged", "user", id, id == Api.Context(http).UserId ? "Changed own password" : "Reset by an administrator").ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            });
            Parameter(server, HttpMethod.GET, "/users/{userId}/credentials", "List a user's credentials", "Users", async http => await Api.SendAsync(http, await _Database.Identity.CredentialsAsync(Api.Context(http), Api.Parameter(http, "userId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Parameter(server, HttpMethod.POST, "/users/{userId}/credentials", "Create a credential for a user; the token is returned only once", "Users", http => CreateCredentialAsync(http, Api.Parameter(http, "userId")));
            Parameter(server, HttpMethod.PUT, "/users/{userId}/credentials/{credentialId}", "Rename, enable or disable a user's credential", "Users", http => UpdateCredentialAsync(http, Api.Parameter(http, "userId")));
            Parameter(server, HttpMethod.DELETE, "/users/{userId}/credentials/{credentialId}", "Delete a user's credential", "Users", http => DeleteCredentialAsync(http, Api.Parameter(http, "userId")));
            Parameter(server, HttpMethod.GET, "/users/{userId}/sessions", "List a user's active sessions", "Users", async http => await Api.SendAsync(http, await _Database.Identity.SessionsAsync(Api.Context(http), Api.Parameter(http, "userId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Parameter(server, HttpMethod.DELETE, "/sessions/{sessionId}", "Revoke any session in your tenant", "Users", http => RevokeSessionAsync(http));

            // Audit log: tenant administrators see their tenant, system administrators see every tenant.
            Static(server, HttpMethod.GET, "/audit", "Search the security audit log, newest first", "Audit", async http =>
            {
                AuditQuery query = new AuditQuery
                {
                    Operation = http.Request.RetrieveQueryValue("operation") ?? "", Outcome = http.Request.RetrieveQueryValue("outcome") ?? "", UserId = http.Request.RetrieveQueryValue("userId") ?? "", TenantId = http.Request.RetrieveQueryValue("tenantId") ?? "",
                    FromUtc = Time(http.Request.RetrieveQueryValue("fromUtc")), ToUtc = Time(http.Request.RetrieveQueryValue("toUtc")),
                    PageNumber = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), PageSize = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "50", CultureInfo.InvariantCulture)
                };
                await Api.SendAsync(http, await _Database.Identity.AuditAsync(Api.Context(http), query, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            });
            Static(server, HttpMethod.DELETE, "/audit", "Delete audit records older than beforeUtc", "Audit", async http =>
            {
                DateTime before = Time(http.Request.RetrieveQueryValue("beforeUtc")) ?? throw new DomainException(400, "before_required", "Send beforeUtc as an ISO 8601 UTC time.", "beforeUtc");
                int deleted = await _Database.Identity.DeleteAuditAsync(Api.Context(http), before, http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, new DeletedCount { Deleted = deleted }).ConfigureAwait(false);
            });

            // Tenants: system administrators.
            Static(server, HttpMethod.GET, "/tenants", "List tenants", "Tenants", async http => await Api.SendAsync(http, await _Database.Identity.TenantsAsync(Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Static(server, HttpMethod.POST, "/tenants", "Create a tenant", "Tenants", async http =>
            {
                Tenant created = await _Database.Identity.CreateTenantAsync(Api.Context(http), Api.Body<TenantRequest>(http), http.Token).ConfigureAwait(false);
                await AuditAsync(http, "TenantCreated", "tenant", created.Id, created.Name).ConfigureAwait(false);
                await Api.SendAsync(http, created, 201).ConfigureAwait(false);
            });
            Parameter(server, HttpMethod.GET, "/tenants/{tenantId}", "Read a tenant", "Tenants", async http => await Api.SendAsync(http, await _Database.Identity.TenantAsync(Api.Context(http), Api.Parameter(http, "tenantId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false));
            Parameter(server, HttpMethod.PUT, "/tenants/{tenantId}", "Rename, enable or disable a tenant with the expected revision", "Tenants", async http =>
            {
                Tenant updated = await _Database.Identity.UpdateTenantAsync(Api.Context(http), Api.Parameter(http, "tenantId"), Api.Body<TenantRequest>(http), http.Token).ConfigureAwait(false);
                await AuditAsync(http, "TenantUpdated", "tenant", updated.Id, updated.Name + (updated.Active ? "" : " (disabled)")).ConfigureAwait(false);
                await Api.SendAsync(http, updated).ConfigureAwait(false);
            });
            Parameter(server, HttpMethod.DELETE, "/tenants/{tenantId}", "Delete a tenant and everything in it; confirmName must be the tenant's exact name", "Tenants", async http =>
            {
                string id = Api.Parameter(http, "tenantId");
                await _Database.Identity.DeleteTenantAsync(Api.Context(http), id, http.Request.RetrieveQueryValue("confirmName") ?? "", http.Token).ConfigureAwait(false);
                await AuditAsync(http, "TenantDeleted", "tenant", id, "").ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            });
        }

        private async Task CreateCredentialAsync(HttpContextBase http, string userId)
        {
            AuthCredentialCreated created = await _Database.Identity.CreateCredentialAsync(Api.Context(http), userId, Api.Body<AuthCredentialRequest>(http), http.Token).ConfigureAwait(false);
            await AuditAsync(http, "CredentialCreated", "credential", created.Credential.Id, "For user " + userId).ConfigureAwait(false);
            await Api.SendAsync(http, created, 201).ConfigureAwait(false);
        }
        private async Task UpdateCredentialAsync(HttpContextBase http, string userId)
        {
            AuthCredential updated = await _Database.Identity.UpdateCredentialAsync(Api.Context(http), userId, Api.Parameter(http, "credentialId"), Api.Body<AuthCredentialRequest>(http), http.Token).ConfigureAwait(false);
            await AuditAsync(http, updated.Active ? "CredentialUpdated" : "CredentialDisabled", "credential", updated.Id, updated.Name).ConfigureAwait(false);
            await Api.SendAsync(http, updated).ConfigureAwait(false);
        }
        private async Task DeleteCredentialAsync(HttpContextBase http, string userId)
        {
            string id = Api.Parameter(http, "credentialId");
            await _Database.Identity.DeleteCredentialAsync(Api.Context(http), userId, id, http.Token).ConfigureAwait(false);
            await AuditAsync(http, "CredentialDeleted", "credential", id, "For user " + userId).ConfigureAwait(false);
            await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
        }
        private async Task RevokeSessionAsync(HttpContextBase http)
        {
            await _Database.Identity.RevokeSessionAsync(Api.Context(http), Api.Parameter(http, "sessionId"), http.Token).ConfigureAwait(false);
            await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
        }
        private Task AuditAsync(HttpContextBase http, string operation, string resourceType, string resourceId, string reason)
        {
            RequestContext context = Api.Context(http);
            return _Authenticator.RecordAsync(new AuditEvent { TenantId = context.TenantId, UserId = context.UserId, Operation = operation, Outcome = "success", ResourceType = resourceType, ResourceId = resourceId, Reason = reason, PrincipalType = context.PrincipalType, SessionId = context.SessionId, CredentialId = context.CredentialId, AuthScheme = context.AuthScheme, CorrelationId = context.CorrelationId, SourceIp = context.SourceIp, Method = http.Request.Method.ToString().ToUpperInvariant(), Path = http.Request.Url.RawWithoutQuery, RequestType = http.Request.Method.ToString().ToUpperInvariant() + " " + http.Request.Url.RawWithoutQuery, AuthorizationResult = context.IsAdmin || context.IsTenantAdmin ? "Permitted (administrator bypass)" : "Permitted", StatusCode = 200 }, http.Token);
        }
        private static DateTime? Time(string? value)
        { return string.IsNullOrWhiteSpace(value) ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal); }
        private static void Static(Webserver server, HttpMethod method, string path, string summary, string group, Func<HttpContextBase, Task> handler)
        { server.Routes.PostAuthentication.Static.Add(method, Root + path, http => Api.GuardAsync(http, () => handler(http)), openApiMetadata: OpenApiRouteMetadata.Create(summary, group)); }
        private static void Parameter(Webserver server, HttpMethod method, string path, string summary, string group, Func<HttpContextBase, Task> handler)
        { server.Routes.PostAuthentication.Parameter.Add(method, Root + path, http => Api.GuardAsync(http, () => handler(http)), openApiMetadata: OpenApiRouteMetadata.Create(summary, group)); }
    }
}
