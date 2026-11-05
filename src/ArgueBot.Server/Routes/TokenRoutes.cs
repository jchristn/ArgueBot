namespace ArgueBot.Server.Routes
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System.Reflection;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Sign-in, session tokens and the caller's own identity and sessions.</summary>
    internal sealed class TokenRoutes
    {
        private const string Root = "/v2.0/api";
        private readonly DatabaseDriverBase _Database;
        private readonly RequestAuthenticator _Authenticator;
        internal TokenRoutes(DatabaseDriverBase database, RequestAuthenticator authenticator) { _Database = database; _Authenticator = authenticator; }

        internal static string Version() { return typeof(TokenRoutes).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "2.1.0"; }

        internal void Register(Webserver server)
        {
            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, Root + "/token", http => Api.GuardAsync(http, async () =>
            {
                RequestContext received = Api.Context(http);
                LoginRequest request; string scheme;
                string headerEmail = http.Request.Headers["x-email"] ?? "";
                if (headerEmail.Length > 0)
                {
                    request = new LoginRequest { Email = headerEmail, Password = http.Request.Headers["x-password"] ?? "", TenantId = http.Request.Headers["x-tenant-id"] ?? http.Request.Headers["x-tenant-guid"] ?? "" };
                    scheme = "PasswordHeaders";
                }
                else { request = Api.Body<LoginRequest>(http); scheme = "PasswordBody"; }
                TokenResponse response = await _Authenticator.LoginAsync(request, received, scheme, http.Request.Useragent ?? "", http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, response, 201).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Sign in with an email and password and receive a session token", "Authentication"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, Root + "/login-info", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Authenticator.LoginInfoAsync(Version(), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Sign-in page information, including the default credentials", "Authentication"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, Root + "/token", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Check that the bearer token is valid", "Authentication"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, Root + "/token/details", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Authenticator.DetailsAsync(Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Describe the bearer token: user, tenant, principal and expiry", "Authentication"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, Root + "/token/refresh", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Authenticator.RefreshAsync(Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Replace the session token with a new one that has a fresh lifetime", "Authentication"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.DELETE, Root + "/token", http => Api.GuardAsync(http, async () =>
            {
                await _Authenticator.LogoutAsync(Api.Context(http), http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Sign out: revoke the current session token", "Authentication"));
        }
    }
}
