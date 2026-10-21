#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    internal sealed class TerminalJourneyHandler : HttpMessageHandler
    {
        private readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        internal List<RunnerProfile> Profiles { get; } = new List<RunnerProfile>();
        internal bool Deleted { get; private set; }
        internal bool RejectDelete { get; set; }
        internal Conversation? Conversation { get; private set; }
        internal CreateRunRequest? Started { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            object result;
            if (path == "/v1.0/api/participants" && request.Method == HttpMethod.Get) result = Profiles;
            else if (path == "/v1.0/api/participants" && request.Method == HttpMethod.Post)
            {
                RunnerProfile profile = JsonSerializer.Deserialize<RunnerProfile>(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), _Json)!;
                profile.Id = "fixture-agent-" + Profiles.Count; Profiles.Add(profile); result = profile;
            }
            else if (path == "/v1.0/api/conversations" && request.Method == HttpMethod.Post)
            {
                ConversationRequest body = JsonSerializer.Deserialize<ConversationRequest>(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), _Json)!;
                Conversation = new Conversation { Id = "fixture-conversation", Title = body.Title };
                result = Conversation;
            }
            else if (path == "/v1.0/api/conversations/fixture-conversation" && request.Method == HttpMethod.Get) result = Conversation!;
            else if (path == "/v1.0/api/conversations/fixture-conversation" && request.Method == HttpMethod.Delete)
            {
                if (RejectDelete) return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(JsonSerializer.Serialize(new ApiError { Code = "active_run", Message = "Stop the active or paused run before deleting this conversation." }, _Json), Encoding.UTF8, "application/json") };
                Deleted = true; Conversation = null; result = new DeletionResponse();
            }
            else if (path == "/v1.0/api/conversations/fixture-conversation/runs" && request.Method == HttpMethod.Get)
                result = new List<DebateRun> { new DebateRun { Id = "fixture-run", ConversationId = "fixture-conversation", State = RejectDelete ? "paused" : "completed" } };
            else if (path == "/v1.0/api/conversations/fixture-conversation/runs" && request.Method == HttpMethod.Post)
            {
                Started = JsonSerializer.Deserialize<CreateRunRequest>(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), _Json)!;
                result = new DebateRun { Id = "fixture-run", ConversationId = "fixture-conversation", State = "queued" };
            }
            else throw new InvalidOperationException("Unexpected terminal journey request: " + request.Method + " " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result, _Json), Encoding.UTF8, "application/json") };
        }
    }
}
#endif
