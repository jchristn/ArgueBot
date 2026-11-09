namespace ArgueBot.Server.Routes
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System.Globalization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class DebateRoutes
    {
        private readonly DatabaseDriverBase _Database;
        private readonly ConversationEngine _Engine;
        internal DebateRoutes(DatabaseDriverBase database, ConversationEngine engine) { _Database = database; _Engine = engine; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v2.0/api/capabilities", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, new ServiceCapabilities { MaxParticipants = _Database.Debates.MaximumParticipants }).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read multi-agent capabilities and admission limits", "Debates"));
            string root = "/v2.0/api/conversations/{conversationId}";
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/interjections", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Engine.InterjectAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Body<InterjectionRequest>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Accept context with an explicit delivery mode", "Interjections"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/interjections", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Interjections.EnumerateAsync(Api.Context(http), Api.Parameter(http, "conversationId"), int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read context receipts and participant deliveries", "Interjections"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/runs/{runId}/roster", http => Api.GuardAsync(http, async () =>
            {
                await _Engine.ChangeRosterAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "runId"), Api.Body<RosterChangeRequest>(http), http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement(), 202).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Queue add, remove or replace at a safe boundary", "Roster"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/runs/{runId}/roster", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Rosters.EnumerateAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "runId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Inspect current and historical seats", "Roster"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/runs/{runId}/summaries", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Memory.EnumerateAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "runId"), int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), http.Token, http.Request.RetrieveQueryValue("id") ?? "", int.Parse(http.Request.RetrieveQueryValue("sourcePageNumber") ?? "1", CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read summary text and original source provenance", "Memory"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/runs/{runId}/proposals", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Debates.ReadProposalsAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "runId"), http.Token, int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Inspect proposals, agreement and dissent", "Consensus"));
        }
    }
}
