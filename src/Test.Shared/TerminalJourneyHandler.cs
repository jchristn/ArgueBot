#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class TerminalJourneyHandler : HttpMessageHandler
    {
        private readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        internal List<RunnerProfile> Profiles { get; } = new List<RunnerProfile>();
        internal List<ModelProfile> Models { get; } = new List<ModelProfile>();
        internal List<EndpointProfile> Endpoints { get; } = new List<EndpointProfile>();
        internal List<ModelCheck> Checks { get; } = new List<ModelCheck>();
        internal bool KeyRotated { get; private set; }
        internal bool Deleted { get; private set; }
        internal bool RejectDelete { get; set; }
        internal Conversation? Conversation { get; private set; }
        internal CreateRunRequest? Started { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            object result;
            if (path == "/v2.0/api/capabilities") result = new ServiceCapabilities();
            else if (path == "/v2.0/api/configuration/credential-keys") result = new CredentialKeyStatus { ActiveKeyId = "fixture-master-key", RetainedKeyCount = 1 };
            else if (path == "/v2.0/api/configuration/credential-keys/rotate") { KeyRotated = true; result = new CredentialKeyStatus { ActiveKeyId = "rotated-fixture-key", RetainedKeyCount = 2, ReencryptedCredentials = 3 }; }
            else if (path == "/v2.0/api/configuration/models") result = Models;
            else if (path == "/v2.0/api/configuration/endpoints") result = Endpoints;
            else if (path == "/v2.0/api/configuration/endpoints/endpoint_fixture/preview") result = EndpointRequests.Preview(Endpoints[0]);
            else if (path == "/v2.0/api/configuration/credentials") result = new List<CredentialMetadata>();
            else if (path == "/v2.0/api/configuration/models/model_fixture/checks" && request.Method == HttpMethod.Get) result = Checks;
            else if (path == "/v2.0/api/configuration/models/model_fixture/checks")
            {
                ModelCheckRequest check = JsonSerializer.Deserialize<ModelCheckRequest>(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), _Json)!;
                ModelCheck observed = new ModelCheck { Id = "check_fixture", ModelId = "model_fixture", ModelRevision = 1, TransportRevision = 1, State = "completed", CheckTools = check.CheckTools, CompletionVerified = true, ToolsVerified = check.CheckTools, CreatedUtc = DateTime.UtcNow };
                Checks.Add(observed); result = observed;
            }
            else if (path == "/v1.0/api/participants" && request.Method == HttpMethod.Get) result = Profiles;
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
            else if (path == "/v2.0/api/conversations/fixture-conversation/runs" && request.Method == HttpMethod.Post)
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
