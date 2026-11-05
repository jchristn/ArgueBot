namespace ArgueBot.Server.Routes
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Time-bound public links: members create, list and revoke them; anyone with a link reads the transcript.</summary>
    internal sealed class ShareRoutes
    {
        private const string Root = "/v2.0/api";
        private readonly DatabaseDriverBase _Database;
        private readonly bool _Tls;
        // Public reads are limited per address so link tokens cannot be guessed quickly; a wrong token costs the same as a right one.
        private readonly LoginRateLimiter _Reads = new LoginRateLimiter(120, TimeSpan.FromMinutes(1));
        internal ShareRoutes(DatabaseDriverBase database, bool tls) { _Database = database; _Tls = tls; }

        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Root + "/conversations/{conversationId}/shares", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Shares.ListAsync(Api.Context(http), Api.Parameter(http, "conversationId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List a conversation's public links", "Sharing"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, Root + "/conversations/{conversationId}/shares", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Shares.CreateAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Body<ConversationShareRequest>(http), BaseUrl(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create a time-bound public link", "Sharing"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, Root + "/conversations/{conversationId}/shares/{shareId}/reveal", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Shares.RevealAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "shareId"), BaseUrl(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Show an active public link again", "Sharing"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, Root + "/conversations/{conversationId}/shares/{shareId}", http => Api.GuardAsync(http, async () =>
            {
                await _Database.Shares.RevokeAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "shareId"), http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Revoke a public link", "Sharing"));
            server.Routes.PreAuthentication.Parameter.Add(HttpMethod.GET, Root + "/shares/{shareToken}", http => Api.GuardAsync(http, async () =>
            {
                string key = "share:" + Api.Context(http).SourceIp;
                _Reads.Check(key); _Reads.Fail(key);
                http.Response.Headers["Referrer-Policy"] = "no-referrer";
                await Api.SendAsync(http, await _Database.Shares.ReadAsync(Api.Parameter(http, "shareToken"), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Read the transcript behind a public link; needs no sign-in", "Sharing"));
        }

        /// <summary>The origin the caller used, so links created through a proxy name point there.</summary>
        private string BaseUrl(HttpContextBase http)
        {
            string forwarded = (http.Request.Headers["X-Forwarded-Proto"] ?? "").Split(',')[0].Trim();
            string scheme = _Tls || forwarded == "https" ? "https" : "http";
            return scheme + "://" + (http.Request.Headers["Host"] ?? "127.0.0.1");
        }
    }
}
