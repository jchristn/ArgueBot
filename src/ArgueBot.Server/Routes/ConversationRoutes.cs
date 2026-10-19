namespace ArgueBot.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class ConversationRoutes
    {
        private readonly SqliteDatabaseDriver _Database;
        private readonly ConversationEngine _Engine;
        internal ConversationRoutes(SqliteDatabaseDriver database, ConversationEngine engine) { _Database = database; _Engine = engine; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/conversations", http => Api.GuardAsync(http, async () =>
            {
                string search = http.Request.Query.Elements["search"] ?? "";
                int page = int.TryParse(http.Request.Query.Elements["pageNumber"], out int parsedPage) ? parsedPage : 1;
                int size = int.TryParse(http.Request.Query.Elements["pageSize"], out int parsedSize) ? parsedSize : 25;
                bool archive = http.Request.Query.Elements["archived"] == "true";
                await Api.SendAsync(http, await _Database.EnumerateConversationsAsync(Api.Context(http), search, archive, page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Search owned conversations", "Conversations"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/conversations", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.CreateConversationAsync(Api.Context(http), Api.Body<ConversationRequest>(http).Title, http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create a conversation", "Conversations"));
            string root = "/v1.0/api/conversations/{conversationId}";
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root, http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.ReadConversationAsync(Api.Context(http), Api.Parameter(http, "conversationId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read conversation", "Conversations"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, root, http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.UpdateConversationAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Body<ConversationRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update conversation with expected revision", "Conversations"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, root, http => Api.GuardAsync(http, async () =>
            { await _Database.DeleteConversationAsync(Api.Context(http), Api.Parameter(http, "conversationId"), http.Token).ConfigureAwait(false); await Api.SendAsync(http, new { deleted = true }).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Delete stopped conversation", "Conversations"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/messages", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.ReadMessagesAsync(Api.Context(http), Api.Parameter(http, "conversationId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read ordered messages", "Messages"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/messages", http => Api.GuardAsync(http, async () =>
            {
                CommandRequest request = Api.Body<CommandRequest>(http);
                if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.Action != "note") throw new DomainException(400, "invalid_message", "Use action note with text and idempotencyKey; directives use run actions.");
                await Api.SendAsync(http, await _Database.AppendMessageAsync(Api.Context(http), new Message { ConversationId = Api.Parameter(http, "conversationId"), Content = request.Text, Kind = "note" }, request.IdempotencyKey, http.Token).ConfigureAwait(false), 201).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Append idempotent public note", "Messages"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/events", http => Api.GuardAsync(http, async () =>
            {
                long after = long.TryParse(http.Request.Query.Elements["after"], out long parsed) ? parsed : 0;
                await Api.SendAsync(http, await _Database.ReadEventsAsync(Api.Context(http), Api.Parameter(http, "conversationId"), after, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Read durable event page after cursor", "Events"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/runs", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Runs.EnumerateAsync(Api.Context(http), Api.Parameter(http, "conversationId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List conversation runs", "Runs"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/runs", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Engine.StartAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Body<CreateRunRequest>(http), http.Token).ConfigureAwait(false), 202).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Start durable debate run", "Runs"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/runs/{runId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Runs.ReadAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "runId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read run checkpoint", "Runs"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/runs/{runId}/actions", http => Api.GuardAsync(http, async () =>
            {
                await _Engine.ApplyAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "runId"), Api.Body<CommandRequest>(http), http.Request.Headers["x-client-id"] ?? "default", http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, new { accepted = true }).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Apply moderator command", "Runs"));
        }
    }
}
