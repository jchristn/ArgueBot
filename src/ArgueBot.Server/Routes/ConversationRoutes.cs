namespace ArgueBot.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class ConversationRoutes
    {
        private readonly DatabaseDriverBase _Database;
        private readonly ConversationEngine _Engine;
        private readonly SessionMethods _Sessions;
        internal ConversationRoutes(DatabaseDriverBase database, ConversationEngine engine, SessionMethods sessions) { _Database = database; _Engine = engine; _Sessions = sessions; }
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
            { await _Database.DeleteConversationAsync(Api.Context(http), Api.Parameter(http, "conversationId"), http.Token).ConfigureAwait(false); await Api.SendAsync(http, new DeletionResponse()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Delete stopped conversation", "Conversations"));
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
                if ((http.Request.Headers["Accept"] ?? "").Contains("text/event-stream", StringComparison.Ordinal))
                    await StreamAsync(http, after).ConfigureAwait(false);
                else await Api.SendAsync(http, await _Database.ReadEventsAsync(Api.Context(http), Api.Parameter(http, "conversationId"), after, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
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
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Apply moderator command", "Runs"));
        }
        private async Task StreamAsync(HttpContextBase http, long after)
        {
            string conversationId = Api.Parameter(http, "conversationId");
            string resume = http.Request.Headers["Last-Event-ID"] ?? "";
            if (resume.Length > 0)
            {
                string[] cursor = resume.Split(':');
                if (cursor.Length != 2 || cursor[0] != conversationId || !long.TryParse(cursor[1], out after) || after < 0)
                    throw new DomainException(400, "invalid_cursor", "Resume cursor must belong to this conversation.");
            }
            await _Database.ReadConversationAsync(Api.Context(http), conversationId, http.Token).ConfigureAwait(false);
            http.Response.StatusCode = 200; http.Response.ContentType = "text/event-stream";
            http.Response.ChunkedTransfer = false; http.Response.ServerSentEvents = true;
            DateTime deadline = DateTime.UtcNow.AddSeconds(30); DateTime heartbeat = DateTime.MinValue;
            while (!http.Token.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                RequestContext? current = await _Sessions.AuthenticateAsync(Api.SessionToken(http), http.Token).ConfigureAwait(false);
                if (current == null)
                {
                    await http.Response.SendEvent(new ServerSentEvent { Event = "session_revoked", Data = "{}" }, true, http.Token).ConfigureAwait(false); return;
                }
                List<DurableEvent> events = await _Database.ReadEventsAsync(current, conversationId, after, http.Token).ConfigureAwait(false);
                foreach (DurableEvent entry in events)
                {
                    bool sent = await http.Response.SendEvent(new ServerSentEvent { Id = conversationId + ":" + entry.Sequence.ToString(CultureInfo.InvariantCulture), Event = "change", Data = JsonSerializer.Serialize(entry, Api.Json) }, false, http.Token).ConfigureAwait(false);
                    if (!sent) return; after = entry.Sequence;
                }
                if (DateTime.UtcNow >= heartbeat)
                {
                    if (!await http.Response.SendEvent(new ServerSentEvent { Event = "heartbeat", Data = "{}" }, false, http.Token).ConfigureAwait(false)) return;
                    heartbeat = DateTime.UtcNow.AddSeconds(5);
                }
                await Task.Delay(200, http.Token).ConfigureAwait(false);
            }
            if (!http.Token.IsCancellationRequested) await http.Response.SendEvent(new ServerSentEvent { Event = "reconnect", Data = "{}" }, true, http.Token).ConfigureAwait(false);
        }
    }
}
