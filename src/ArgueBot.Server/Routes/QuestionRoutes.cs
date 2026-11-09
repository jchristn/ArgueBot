namespace ArgueBot.Server.Routes
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Questions agents ask the user: the tenant inbox, one request, answering, skipping, answering later and a live change stream.</summary>
    internal sealed class QuestionRoutes
    {
        private const string Root = "/v2.0/api/questions";
        private readonly DatabaseDriverBase _Database;
        private readonly ConversationEngine _Engine;
        internal QuestionRoutes(DatabaseDriverBase database, ConversationEngine engine) { _Database = database; _Engine = engine; }

        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, Root, http => Api.GuardAsync(http, async () =>
            {
                QuestionFilter filter = new QuestionFilter
                {
                    Status = http.Request.RetrieveQueryValue("status") ?? "",
                    ConversationId = http.Request.RetrieveQueryValue("conversationId") ?? "",
                    RunId = http.Request.RetrieveQueryValue("runId") ?? "",
                    Scope = http.Request.RetrieveQueryValue("scope") ?? "mine",
                    PageNumber = Number(http, "pageNumber", 1),
                    PageSize = Number(http, "pageSize", 25)
                };
                await Api.SendAsync(http, await _Database.Questions.EnumerateAsync(Api.Context(http), filter, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("List questions agents asked, open first", "Questions"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, Root + "/summary", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Questions.SummaryAsync(Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Count open questions for badges", "Questions"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, Root + "/events", http => Api.GuardAsync(http, async () =>
            {
                string after = http.Request.Headers["Last-Event-ID"] ?? http.Request.RetrieveQueryValue("after") ?? "";
                int limit = Number(http, "limit", 100);
                if ((http.Request.Headers["Accept"] ?? "").Contains("text/event-stream", StringComparison.Ordinal)) await StreamAsync(http, after, limit).ConfigureAwait(false);
                else await Api.SendAsync(http, await _Database.Questions.ReadEventsAsync(Api.Context(http), after, limit, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Read or stream question changes in the tenant", "Questions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Root + "/{questionId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Questions.ReadAsync(Api.Context(http), Api.Parameter(http, "questionId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read a question request with its answers", "Questions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, Root + "/{questionId}/answer", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Engine.AnswerQuestionAsync(Api.Context(http), Api.Parameter(http, "questionId"), Api.Body<QuestionAnswerRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Answer every question of a request", "Questions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, Root + "/{questionId}/skip", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Engine.SkipQuestionAsync(Api.Context(http), Api.Parameter(http, "questionId"), Api.Body<QuestionSkipRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Skip a question request so the agent continues on assumptions", "Questions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, Root + "/{questionId}/defer", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Engine.DeferQuestionAsync(Api.Context(http), Api.Parameter(http, "questionId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Answer later: end the waiting turn now and resume after the answer", "Questions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v2.0/api/conversations/{conversationId}/runs/{runId}/questions", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Questions.RunRequestsAsync(Api.Context(http), Api.Parameter(http, "conversationId"), Api.Parameter(http, "runId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List the questions a run's agents asked", "Questions"));
        }

        private static int Number(HttpContextBase http, string name, int fallback)
        {
            string? value = http.Request.RetrieveQueryValue(name);
            if (value == null) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)) throw new DomainException(400, "invalid_page", "Use a whole number for " + name + ".", name);
            return result;
        }

        private async Task StreamAsync(HttpContextBase http, string after, int limit)
        {
            QuestionEventPage page = await _Database.Questions.ReadEventsAsync(Api.Context(http), after, limit, http.Token).ConfigureAwait(false);
            http.Response.StatusCode = 200; http.Response.ContentType = "text/event-stream";
            http.Response.ChunkedTransfer = false; http.Response.ServerSentEvents = true;
            long started = Stopwatch.GetTimestamp(); long heartbeat = started; bool first = true;
            string cursor = page.Next;
            while (!http.Token.IsCancellationRequested && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(ServiceProtocol.EventConnectionSeconds))
            {
                foreach (QuestionEvent entry in page.Items)
                {
                    if (!await http.Response.SendEvent(new ServerSentEvent { Id = entry.Cursor, Event = "question", Data = JsonSerializer.Serialize(entry, Api.Json) }, false, http.Token).ConfigureAwait(false)) return;
                }
                if (first || Stopwatch.GetElapsedTime(heartbeat) >= TimeSpan.FromSeconds(ServiceProtocol.EventHeartbeatSeconds))
                {
                    // The first heartbeat carries the cursor so a client can resume without missing changes.
                    if (!await http.Response.SendEvent(new ServerSentEvent { Id = cursor, Event = "heartbeat", Data = "{}" }, false, http.Token).ConfigureAwait(false)) return;
                    heartbeat = Stopwatch.GetTimestamp(); first = false;
                }
                await Task.Delay(500, http.Token).ConfigureAwait(false);
                page = await _Database.Questions.ReadEventsAsync(Api.Context(http), cursor, limit, http.Token).ConfigureAwait(false);
                cursor = page.Next;
            }
            if (!http.Token.IsCancellationRequested) await http.Response.SendEvent(new ServerSentEvent { Event = "reconnect", Data = "{}" }, true, http.Token).ConfigureAwait(false);
        }
    }
}
