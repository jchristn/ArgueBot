namespace ArgueBot.Client
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Shared typed HTTP client for TUI and console surfaces. Owns its HTTP connection pool.</summary>
    public sealed class ArgueBotClient : IDisposable
    {
        private readonly HttpClient _Http;
        private volatile string _Token = "";
        private volatile string _TenantId = "";
        /// <summary>Tenant sent as x-tenant-id on every request. Leave empty for the token's own tenant; system administrators set it to manage another tenant's users, sessions and audit log.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value.Trim(); } }
        /// <summary>Bearer token (session or credential) sent as Authorization: Bearer on every request and event stream. Empty sends none.</summary>
        public string Token { get { return _Token; } set { ArgumentNullException.ThrowIfNull(value); _Token = value.Trim(); } }
        private readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        private async Task<List<T>> ReadListPagesAsync<T>(string route, int pageSize, CancellationToken token)
        {
            List<T> result = new List<T>();
            for (int page = 1; ; page++)
            {
                List<T> items = await SendAsync<List<T>>(HttpMethod.Get, route + "?pageNumber=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture), token: token).ConfigureAwait(false);
                result.AddRange(items);
                if (items.Count < pageSize) return result;
            }
        }
        private async Task<List<T>> ReadConfigurationAsync<T>(string resource, CancellationToken token)
        {
            List<T> result = new List<T>();
            for (int page = 1; ; page++)
            {
                List<T> items = await SendAsync<List<T>>(HttpMethod.Get, "v2.0/api/configuration/" + resource + "?pageSize=100&pageNumber=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture), token: token).ConfigureAwait(false);
                result.AddRange(items);
                if (items.Count < 100) return result;
            }
        }
        /// <summary>Preview saved request destinations and binding names without contacting the endpoint or resolving credentials.</summary>
        public Task<EndpointPreview> PreviewEndpointAsync(string id, CancellationToken token = default)
        { return SendAsync<EndpointPreview>(HttpMethod.Get, "v2.0/api/configuration/endpoints/" + Uri.EscapeDataString(id) + "/preview", token: token); }
        /// <summary>Contact a saved endpoint to list advertised models without generating a completion.</summary>
        public Task<EndpointDiscovery> DiscoverEndpointAsync(string id, CancellationToken token = default)
        { return SendAsync<EndpointDiscovery>(HttpMethod.Post, "v2.0/api/configuration/endpoints/" + Uri.EscapeDataString(id) + "/discover", token: token); }
        /// <summary>List reusable endpoints.</summary>
        public Task<List<EndpointProfile>> EndpointsAsync(CancellationToken token = default)
        { return ReadConfigurationAsync<EndpointProfile>("endpoints", token); }
        /// <summary>Create or revision-check a reusable endpoint.</summary>
        public Task<EndpointProfile> SaveEndpointAsync(EndpointProfile value, CancellationToken token = default)
        { return SendAsync<EndpointProfile>(value.Id.Length == 0 ? HttpMethod.Post : HttpMethod.Put, "v2.0/api/configuration/endpoints" + (value.Id.Length == 0 ? "" : "/" + Uri.EscapeDataString(value.Id)), value, token); }
        /// <summary>Read a bounded page of observed checks tied to exact model and transport revisions.</summary>
        public Task<List<ModelCheck>> ModelChecksAsync(string modelId, int pageNumber = 1, CancellationToken token = default)
        { return SendAsync<List<ModelCheck>>(HttpMethod.Get, "v2.0/api/configuration/models/" + Uri.EscapeDataString(modelId) + "/checks?pageNumber=" + pageNumber, token: token); }
        /// <summary>Explicitly invoke a potentially billable completion and optional harmless tool check.</summary>
        public Task<ModelCheck> CheckModelAsync(string modelId, ModelCheckRequest request, CancellationToken token = default)
        { return SendAsync<ModelCheck>(HttpMethod.Post, "v2.0/api/configuration/models/" + Uri.EscapeDataString(modelId) + "/checks", request, token); }
        /// <summary>Read a bounded page of observed checks of a saved agent.</summary>
        public Task<List<ModelCheck>> AgentChecksAsync(string profileId, int pageNumber = 1, CancellationToken token = default)
        { return SendAsync<List<ModelCheck>>(HttpMethod.Get, "v2.0/api/participants/" + Uri.EscapeDataString(profileId) + "/checks?pageNumber=" + pageNumber, token: token); }
        /// <summary>Explicitly run a saved agent once, with its secret and model. This can cost money.</summary>
        public Task<ModelCheck> CheckAgentAsync(string profileId, ModelCheckRequest request, CancellationToken token = default)
        { return SendAsync<ModelCheck>(HttpMethod.Post, "v2.0/api/participants/" + Uri.EscapeDataString(profileId) + "/checks", request, token); }
        /// <summary>List reusable models.</summary>
        public Task<List<ModelProfile>> ModelsAsync(CancellationToken token = default)
        { return ReadConfigurationAsync<ModelProfile>("models", token); }
        /// <summary>Create or revision-check a reusable model.</summary>
        public Task<ModelProfile> SaveModelAsync(ModelProfile value, CancellationToken token = default)
        { return SendAsync<ModelProfile>(value.Id.Length == 0 ? HttpMethod.Post : HttpMethod.Put, "v2.0/api/configuration/models" + (value.Id.Length == 0 ? "" : "/" + Uri.EscapeDataString(value.Id)), value, token); }
        /// <summary>Read safe master-key identities without key material.</summary>
        public Task<CredentialKeyStatus> CredentialKeysAsync(CancellationToken token = default)
        { return SendAsync<CredentialKeyStatus>(HttpMethod.Get, "v2.0/api/configuration/credential-keys", token: token); }
        /// <summary>Rotate the expected active master key and reencrypt stored credentials.</summary>
        public Task<CredentialKeyStatus> RotateCredentialKeyAsync(string expectedKeyId, CancellationToken token = default)
        { return SendAsync<CredentialKeyStatus>(HttpMethod.Post, "v2.0/api/configuration/credential-keys/rotate", new RotateCredentialKeyRequest { ExpectedKeyId = expectedKeyId }, token); }
        /// <summary>Retire earlier master keys once every credential uses the active key. Older backups can no longer decrypt their credentials.</summary>
        public Task<CredentialKeyStatus> RetireCredentialKeysAsync(string expectedKeyId, CancellationToken token = default)
        { return SendAsync<CredentialKeyStatus>(HttpMethod.Post, "v2.0/api/configuration/credential-keys/retire", new RotateCredentialKeyRequest { ExpectedKeyId = expectedKeyId }, token); }
        /// <summary>List safe credential metadata; no values are returned.</summary>
        public Task<List<CredentialMetadata>> CredentialsAsync(CancellationToken token = default)
        { return ReadConfigurationAsync<CredentialMetadata>("credentials", token); }
        /// <summary>Import or replace a write-only credential.</summary>
        public Task<CredentialMetadata> SaveCredentialAsync(string id, CredentialRequest value, CancellationToken token = default)
        { return SendAsync<CredentialMetadata>(id.Length == 0 ? HttpMethod.Post : HttpMethod.Put, "v2.0/api/configuration/credentials" + (id.Length == 0 ? "" : "/" + Uri.EscapeDataString(id)), value, token); }
        /// <summary>Reveal a ready credential's decrypted value. Handle the result as a secret.</summary>
        public Task<RevealedCredential> RevealCredentialAsync(string id, CancellationToken token = default)
        { return SendAsync<RevealedCredential>(HttpMethod.Post, "v2.0/api/configuration/credentials/" + Uri.EscapeDataString(id) + "/reveal", token: token); }
        /// <summary>Disable a credential with an expected revision.</summary>
        public Task<Acknowledgement> DisableCredentialAsync(string id, long revision, CancellationToken token = default)
        { return SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/configuration/credentials/" + Uri.EscapeDataString(id) + "?revision=" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture), token: token); }
        /// <summary>Create a client for an explicit server; loopback default is 127.0.0.1:4317. The client owns its HTTP pool and any supplied handler. Remote servers need HTTPS unless <paramref name="allowInsecureRemote"/> is set, because passwords and tokens would otherwise cross the network unencrypted.</summary>
        public ArgueBotClient(string endpoint = "http://127.0.0.1:4317", HttpMessageHandler? handler = null, bool allowInsecureRemote = false)
        {
            Uri address = new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute);
            if ((address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) || address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0)
                throw new ArgumentException("Use an HTTP service URL without embedded credentials, query or fragment.", nameof(endpoint));
            if (!address.IsLoopback && address.Scheme != Uri.UriSchemeHttps && !allowInsecureRemote)
                throw new ArgumentException("Remote servers need HTTPS: sign-in would send your password and token unencrypted. Use an https:// URL, or allow plain HTTP explicitly (--allow-http) on a network you trust.", nameof(endpoint));
            _Http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler, true);
            _Http.BaseAddress = address; _Http.Timeout = TimeSpan.FromSeconds(30);
            _Http.DefaultRequestHeaders.Add("x-client-id", Guid.NewGuid().ToString("N"));
        }
        /// <summary>Read a bounded newest-first page of generation attempts, including the exact ordered source message identifiers.</summary>
        public Task<AttemptPage> AttemptsAsync(string conversationId, string runId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        { return SendAsync<AttemptPage>(HttpMethod.Get, "v1.0/api/conversations/" + Uri.EscapeDataString(conversationId) + "/runs/" + Uri.EscapeDataString(runId) + "/attempts?pageNumber=" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&pageSize=" + pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), token: token); }
        /// <summary>List matching conversations from the server, with exact totals.</summary>
        public Task<Page> ConversationsAsync(string search = "", bool archived = false, int page = 1, CancellationToken token = default)
        { return SendAsync<Page>(HttpMethod.Get, "v1.0/api/conversations?search=" + Uri.EscapeDataString(search) + "&archived=" + archived.ToString().ToLowerInvariant() + "&pageNumber=" + page, null, token); }
        /// <summary>Create a named conversation.</summary>
        public Task<Conversation> CreateConversationAsync(string title, CancellationToken token = default, string openingQuestion = "", Dictionary<string, string>? promptIds = null)
        { return SendAsync<Conversation>(HttpMethod.Post, "v1.0/api/conversations", new ConversationRequest { Title = title, OpeningQuestion = openingQuestion, PromptIds = promptIds }, token); }
        /// <summary>List factory and workspace prompts, optionally for one debate step.</summary>
        public Task<List<PromptTemplate>> PromptsAsync(string purpose = "", CancellationToken token = default)
        { return SendAsync<List<PromptTemplate>>(HttpMethod.Get, "v2.0/api/prompts" + (purpose.Length == 0 ? "" : "?purpose=" + Uri.EscapeDataString(purpose)), token: token); }
        /// <summary>Read one prompt.</summary>
        public Task<PromptTemplate> PromptAsync(string id, CancellationToken token = default)
        { return SendAsync<PromptTemplate>(HttpMethod.Get, "v2.0/api/prompts/" + Uri.EscapeDataString(id), token: token); }
        /// <summary>Create a prompt when its ID is empty, otherwise update it with its expected revision.</summary>
        public Task<PromptTemplate> SavePromptAsync(PromptTemplate prompt, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(prompt); return SendAsync<PromptTemplate>(prompt.Id.Length == 0 ? HttpMethod.Post : HttpMethod.Put, "v2.0/api/prompts" + (prompt.Id.Length == 0 ? "" : "/" + Uri.EscapeDataString(prompt.Id)), prompt, token); }
        /// <summary>Copy a prompt, including a factory default, into an editable prompt.</summary>
        public Task<PromptTemplate> DuplicatePromptAsync(string id, CancellationToken token = default)
        { return SendAsync<PromptTemplate>(HttpMethod.Post, "v2.0/api/prompts/" + Uri.EscapeDataString(id) + "/duplicate", token: token); }
        /// <summary>Delete a workspace prompt with its expected revision.</summary>
        public Task<Acknowledgement> DeletePromptAsync(string id, long revision, CancellationToken token = default)
        { return SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/prompts/" + Uri.EscapeDataString(id) + "?revision=" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture), token: token); }
        /// <summary>List prompt placeholders and where each has a value.</summary>
        public Task<List<PromptParameter>> PromptParametersAsync(CancellationToken token = default)
        { return SendAsync<List<PromptParameter>>(HttpMethod.Get, "v2.0/api/prompt-parameters", token: token); }
        /// <summary>Page the tenant's resources by name, optionally filtered by search text and tag.</summary>
        public Task<ResourcePage> ResourcesAsync(string search = "", string tag = "", int pageNumber = 1, int pageSize = 50, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(search); ArgumentNullException.ThrowIfNull(tag);
            string query = "?pageNumber=" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&pageSize=" + pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + (search.Length == 0 ? "" : "&search=" + Uri.EscapeDataString(search)) + (tag.Length == 0 ? "" : "&tag=" + Uri.EscapeDataString(tag));
            return SendAsync<ResourcePage>(HttpMethod.Get, "v2.0/api/resources" + query, token: token);
        }
        /// <summary>Every resource summary, reading all pages.</summary>
        public async Task<List<ResourceSummary>> AllResourcesAsync(string search = "", string tag = "", CancellationToken token = default)
        {
            List<ResourceSummary> result = new List<ResourceSummary>();
            for (int page = 1; ; page++)
            {
                ResourcePage current = await ResourcesAsync(search, tag, page, 200, token).ConfigureAwait(false);
                result.AddRange(current.Items);
                if (current.Items.Count == 0 || result.Count >= current.TotalCount) return result;
            }
        }
        /// <summary>Read a resource by ID (res_...) or, for anything else, by name.</summary>
        public Task<Resource> ResourceAsync(string idOrName, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(idOrName); return SendAsync<Resource>(HttpMethod.Get, ResourcePath(idOrName), token: token); }
        /// <summary>Create a resource.</summary>
        public Task<Resource> CreateResourceAsync(ResourceRequest request, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(request); return SendAsync<Resource>(HttpMethod.Post, "v2.0/api/resources", request, token); }
        /// <summary>Change a resource by ID with its expected revision in <paramref name="request"/>. Null fields keep their values.</summary>
        public Task<Resource> UpdateResourceAsync(string id, ResourceRequest request, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(request); return SendAsync<Resource>(HttpMethod.Put, "v2.0/api/resources/" + Uri.EscapeDataString(id), request, token); }
        /// <summary>Delete a resource and its history by ID with its expected revision.</summary>
        public Task<Acknowledgement> DeleteResourceAsync(string id, long revision, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(id); return SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/resources/" + Uri.EscapeDataString(id) + "?revision=" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture), token: token); }
        /// <summary>A resource's saved revisions, newest first, without bodies.</summary>
        public Task<List<ResourceRevision>> ResourceRevisionsAsync(string id, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(id); return SendAsync<List<ResourceRevision>>(HttpMethod.Get, "v2.0/api/resources/" + Uri.EscapeDataString(id) + "/revisions", token: token); }
        /// <summary>One saved revision with its body.</summary>
        public Task<ResourceRevision> ResourceRevisionAsync(string id, long revision, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(id); return SendAsync<ResourceRevision>(HttpMethod.Get, "v2.0/api/resources/" + Uri.EscapeDataString(id) + "/revisions/" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture), token: token); }
        /// <summary>Restore a saved revision as a new revision.</summary>
        public Task<Resource> RevertResourceAsync(string id, ResourceRevertRequest request, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(request); return SendAsync<Resource>(HttpMethod.Post, "v2.0/api/resources/" + Uri.EscapeDataString(id) + "/revert", request, token); }
        /// <summary>Import markdown files as resources in one transaction.</summary>
        public Task<ResourceImportResult> ImportResourcesAsync(ResourceImportRequest request, CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(request); return SendAsync<ResourceImportResult>(HttpMethod.Post, "v2.0/api/resources/import", request, token); }
        /// <summary>Export a resource by ID as markdown with front matter.</summary>
        public async Task<string> ExportResourceAsync(string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(id);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "v2.0/api/resources/" + Uri.EscapeDataString(id) + "/export"))
            {
                Authorize(request);
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) await FailAsync(response, token).ConfigureAwait(false);
                    return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                }
            }
        }
        private static string ResourcePath(string idOrName)
        {
            if (idOrName.StartsWith("res_", StringComparison.Ordinal)) return "v2.0/api/resources/" + Uri.EscapeDataString(idOrName);
            return "v2.0/api/resources/by-name/" + string.Join("/", idOrName.Trim().Split('/').Select(Uri.EscapeDataString));
        }
        /// <summary>Read a conversation.</summary>
        public Task<Conversation> ConversationAsync(string id, CancellationToken token = default)
        { return SendAsync<Conversation>(HttpMethod.Get, Root(id), null, token); }
        /// <summary>Update title/pin/archive using its current revision.</summary>
        public Task<Conversation> UpdateConversationAsync(Conversation conversation, CancellationToken token = default)
        { return SendAsync<Conversation>(HttpMethod.Put, Root(conversation.Id), new ConversationRequest { Title = conversation.Title, OpeningQuestion = conversation.OpeningQuestion, PromptIds = conversation.PromptIds, Pinned = conversation.Pinned, Archived = conversation.Archived, Revision = conversation.Revision }, token); }
        /// <summary>Read one authenticated SSE connection after a committed cursor. Frames are bounded to 1048576 UTF-16 characters, idle reads to fifteen seconds, and identifiers/order are validated. Reconnect after normal completion using the last yielded sequence; expired cursors require a fresh head followed by snapshots. Enumeration cancellation/disposal closes the HTTP body.</summary>
        public async IAsyncEnumerable<DurableEvent> WatchEventsAsync(string id, long after = 0, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (after < 0) throw new ArgumentOutOfRangeException(nameof(after));
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Root(id) + "/events"))
            {
                Authorize(request);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Headers.Add("Last-Event-ID", id + ":" + after.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new DomainException((int)response.StatusCode, "redirect_refused", "Configure the final service endpoint explicitly.");
                        ApiError? error;
                        try { error = await response.Content.ReadFromJsonAsync<ApiError>(_Json, token).ConfigureAwait(false); }
                        catch (JsonException) { error = null; }
                        throw new DomainException((int)response.StatusCode, error?.Code ?? "http_error", error?.Message ?? "The service refused the event stream.");
                    }
                    if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new DomainException(502, "invalid_event", "The service did not return an event stream.");
                    using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    {
                        long sequence = after;
                        await foreach (EventFrame frame in EventFrameReader.ReadAsync(stream, token).ConfigureAwait(false))
                        {
                            if (frame.Name == "session_revoked") throw new DomainException(401, "unauthenticated", "The session was revoked. Sign in again.");
                            if (frame.Name == "error")
                            {
                                ApiError error = ParseFrame<ApiError>(frame.Data);
                                if (string.IsNullOrWhiteSpace(error.Code)) throw new DomainException(502, "invalid_event", "The error frame omitted its failure code.");
                                throw new DomainException(error.Code == "event_history_gap" || error.Code == "invalid_cursor" ? 409 : 503, error.Code, error.Message);
                            }
                            if (frame.Name != "change") continue;
                            DurableEvent change = ParseFrame<DurableEvent>(frame.Data);
                            if (change.ConversationId != id || change.Sequence < 1 || frame.Id != id + ":" + change.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new DomainException(502, "invalid_event", "The event identifier does not match its conversation or sequence.");
                            if (change.Sequence <= sequence) continue;
                            if (sequence > 0 && change.Sequence != sequence + 1) throw new DomainException(409, "event_history_gap", "Committed replay has a gap. Refresh snapshots before reconnecting.");
                            sequence = change.Sequence; yield return change;
                        }
                    }
                }
            }
        }
        private T ParseFrame<T>(string data)
        {
            try { return JsonSerializer.Deserialize<T>(data, _Json) ?? throw new JsonException(); }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            { throw new DomainException(502, "invalid_event", "The event payload does not match its documented contract."); }
        }
        /// <summary>Read bounded history previews. A supplied sequence freezes membership; streaming text can still advance. Tail selects the latest page.</summary>
        public Task<MessagePage> MessagePageAsync(string id, int pageNumber = 1, int pageSize = 25, long? throughSequence = null, bool tail = false, CancellationToken token = default)
        {
            string query = "?pageNumber=" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&pageSize=" + pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&tail=" + (tail ? "true" : "false");
            if (throughSequence.HasValue) query += "&throughSequence=" + throughSequence.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return SendAsync<MessagePage>(HttpMethod.Get, Root(id) + "/messages" + query, token: token);
        }
        /// <summary>Read one complete message in the same ownership scope as its conversation.</summary>
        public Task<Message> MessageAsync(string id, string messageId, CancellationToken token = default)
        { return SendAsync<Message>(HttpMethod.Get, Root(id) + "/messages/" + Uri.EscapeDataString(messageId), token: token); }
        /// <summary>Enumerate full messages in sequence order using bounded pages and a captured membership boundary. Truncated previews are hydrated individually. The caller controls memory use.</summary>
        public async IAsyncEnumerable<Message> EnumerateMessagesAsync(string id, [EnumeratorCancellation] CancellationToken token = default)
        {
            MessagePage page = await MessagePageAsync(id, pageSize: 100, token: token).ConfigureAwait(false);
            long through = page.ThroughSequence; long expected = page.TotalCount; long delivered = 0; long sequence = 0;
            while (delivered < expected)
            {
                if (page.ThroughSequence != through || page.TotalCount != expected || page.Items.Count == 0) throw new DomainException(409, "history_changed", "History membership changed during export. Retry from a fresh snapshot.");
                foreach (Message preview in page.Items)
                {
                    token.ThrowIfCancellationRequested();
                    if (preview.Sequence <= sequence || preview.Sequence > through || ++delivered > expected) throw new DomainException(409, "history_changed", "History order changed during export.");
                    Message message = preview.ContentTruncated ? await MessageAsync(id, preview.Id, token).ConfigureAwait(false) : preview;
                    if (message.ContentTruncated || message.Id != preview.Id || message.Sequence != preview.Sequence) throw new DomainException(409, "history_changed", "The full message did not match its preview.");
                    sequence = message.Sequence; yield return message;
                }
                if (delivered < expected) page = await MessagePageAsync(id, page.PageNumber + 1, 100, through, token: token).ConfigureAwait(false);
            }
        }
        /// <summary>Collect complete ordered history for explicit callers that need it in memory. Interfaces should use bounded pages; exports can enumerate instead.</summary>
        public async Task<List<Message>> MessagesAsync(string id, CancellationToken token = default)
        {
            List<Message> messages = new List<Message>();
            await foreach (Message message in EnumerateMessagesAsync(id, token).ConfigureAwait(false)) messages.Add(message);
            return messages;
        }
        /// <summary>Append an idempotent public note.</summary>
        public Task<Message> NoteAsync(string id, string text, string? key = null, CancellationToken token = default)
        { return SendAsync<Message>(HttpMethod.Post, Root(id) + "/messages", new CommandRequest { Action = "note", Text = text, IdempotencyKey = key ?? Guid.NewGuid().ToString("N") }, token); }
        /// <summary>List participant configurations.</summary>
        public Task<List<RunnerProfile>> ParticipantsAsync(CancellationToken token = default)
        { return SendAsync<List<RunnerProfile>>(HttpMethod.Get, "v1.0/api/participants", null, token); }
        /// <summary>Create a reusable CLI or REST participant.</summary>
        public Task<RunnerProfile> CreateParticipantAsync(RunnerProfile profile, CancellationToken token = default)
        { return SendAsync<RunnerProfile>(HttpMethod.Post, "v1.0/api/participants", profile, token); }
        /// <summary>Read all runs in a conversation.</summary>
        public Task<List<DebateRun>> RunsAsync(string id, CancellationToken token = default)
        { return SendAsync<List<DebateRun>>(HttpMethod.Get, Root(id) + "/runs", null, token); }
        /// <summary>Start a debate, freezing participant configuration.</summary>
        public Task<DebateRun> StartAsync(string id, CreateRunRequest request, CancellationToken token = default)
        { return SendAsync<DebateRun>(HttpMethod.Post, (request.ProtocolVersion == 2 ? V2Root(id) : Root(id)) + "/runs", request, token); }
        /// <summary>Apply a moderator command to a run.</summary>
        public async Task ActionAsync(string id, string runId, CommandRequest request, CancellationToken token = default)
        { if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) request.IdempotencyKey = Guid.NewGuid().ToString("N"); await SendAsync<AcceptedResponse>(HttpMethod.Post, Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/actions", request, token).ConfigureAwait(false); }
        private void Authorize(HttpRequestMessage request)
        {
            string current = _Token;
            if (current.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current);
            string tenant = _TenantId;
            if (tenant.Length > 0) request.Headers.Add("x-tenant-id", tenant);
        }
        /// <summary>Read a typed API response and normalize safe domain failures.</summary>
        public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken token = default)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute) || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\'))
                throw new ArgumentException("API paths must be relative to the configured service.", nameof(path));
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (body != null) request.Content = JsonContent.Create(body, options: _Json);
                Authorize(request);
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) await FailAsync(response, token).ConfigureAwait(false);
                    return await response.Content.ReadFromJsonAsync<T>(_Json, token).ConfigureAwait(false) ?? throw new InvalidDataException("Server returned an empty response.");
                }
            }
        }
        private async Task FailAsync(HttpResponseMessage response, CancellationToken token)
        {
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new DomainException((int)response.StatusCode, "redirect_refused", "Configure the final service URL explicitly; API redirects require a direct endpoint.");
            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            ApiError? error = null;
            try { error = text.Length == 0 ? null : JsonSerializer.Deserialize<ApiError>(text, _Json); }
            catch (JsonException) { }
            if (error?.Code == "tenant_required")
            {
                TenantRequiredError? choice = JsonSerializer.Deserialize<TenantRequiredError>(text, _Json);
                throw new TenantRequiredException(choice?.Tenants ?? new List<TenantChoice>());
            }
            throw new DomainException((int)response.StatusCode, error?.Code ?? "http_error", error?.Message ?? response.ReasonPhrase ?? "Request failed.", error?.Field ?? "");
        }
        /// <summary>Public sign-in page information, including the default credentials. Needs no token.</summary>
        public Task<LoginInfo> LoginInfoAsync(CancellationToken token = default)
        { return SendAsync<LoginInfo>(HttpMethod.Get, "v2.0/api/login-info", token: token); }
        /// <summary>Sign in with an email and password. On success the session token becomes this client's <see cref="Token"/>. Throws <see cref="TenantRequiredException"/> when a tenant must be chosen.</summary>
        public async Task<TokenResponse> LoginAsync(string email, string password, string tenantId = "", CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(email); ArgumentNullException.ThrowIfNull(password); ArgumentNullException.ThrowIfNull(tenantId);
            TokenResponse response = await SendAsync<TokenResponse>(HttpMethod.Post, "v2.0/api/token", new LoginRequest { Email = email, Password = password, TenantId = tenantId }, token).ConfigureAwait(false);
            Token = response.Token;
            return response;
        }
        /// <summary>Replace the session token with a fresh one and use it from now on.</summary>
        public async Task<TokenResponse> RefreshAsync(CancellationToken token = default)
        {
            TokenResponse response = await SendAsync<TokenResponse>(HttpMethod.Post, "v2.0/api/token/refresh", token: token).ConfigureAwait(false);
            Token = response.Token;
            return response;
        }
        /// <summary>Sign out: revoke the current session and clear <see cref="Token"/>.</summary>
        public async Task LogoutAsync(CancellationToken token = default)
        {
            await SendAsync<Acknowledgement>(HttpMethod.Delete, "v2.0/api/token", token: token).ConfigureAwait(false);
            Token = "";
        }
        /// <summary>Reveal a bearer-token credential: your own, or another user's when <paramref name="userId"/> is set and you are a tenant administrator. Audited.</summary>
        public Task<RevealedToken> RevealTokenAsync(string credentialId, string userId = "", CancellationToken token = default)
        { ArgumentNullException.ThrowIfNull(credentialId); ArgumentNullException.ThrowIfNull(userId); return SendAsync<RevealedToken>(HttpMethod.Post, (userId.Length == 0 ? "v2.0/api/me" : "v2.0/api/users/" + Uri.EscapeDataString(userId)) + "/credentials/" + Uri.EscapeDataString(credentialId) + "/reveal", token: token); }
        /// <summary>Show an active public share link again. Audited.</summary>
        public Task<ConversationShareCreated> RevealShareAsync(string conversationId, string shareId, CancellationToken token = default)
        { return SendAsync<ConversationShareCreated>(HttpMethod.Post, "v2.0/api/conversations/" + Uri.EscapeDataString(conversationId) + "/shares/" + Uri.EscapeDataString(shareId) + "/reveal", token: token); }
        /// <summary>Describe the current token: user, tenant, principal and expiry.</summary>
        public Task<TokenDetails> TokenDetailsAsync(CancellationToken token = default)
        { return SendAsync<TokenDetails>(HttpMethod.Get, "v2.0/api/token/details", token: token); }
        /// <summary>Read the server's supported debate modes and roster capacity.</summary>
        public Task<ServiceCapabilities> CapabilitiesAsync(CancellationToken token = default)
        { return SendAsync<ServiceCapabilities>(HttpMethod.Get, "v2.0/api/capabilities", token: token); }
        /// <summary>Persist an interjection with the caller's explicit delivery mode and stable command key.</summary>
        public Task<InterjectionReceipt> InterjectAsync(string id, InterjectionRequest request, CancellationToken token = default)
        { return SendAsync<InterjectionReceipt>(HttpMethod.Post, V2Root(id) + "/interjections", request, token); }
        /// <summary>Read a bounded page of context receipts and per-seat deliveries.</summary>
        public Task<List<InterjectionReceipt>> InterjectionsAsync(string id, int pageNumber = 1, CancellationToken token = default)
        { return SendAsync<List<InterjectionReceipt>>(HttpMethod.Get, V2Root(id) + "/interjections?pageNumber=" + pageNumber, token: token); }
        /// <summary>Queue an add, remove or replacement at the next safe boundary.</summary>
        public Task<Acknowledgement> ChangeRosterAsync(string id, string runId, RosterChangeRequest request, CancellationToken token = default)
        { return SendAsync<Acknowledgement>(HttpMethod.Post, V2Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/roster", request, token); }
        /// <summary>Read current and historical roster membership.</summary>
        public Task<List<DebateSeat>> RosterAsync(string id, string runId, CancellationToken token = default)
        { return SendAsync<List<DebateSeat>>(HttpMethod.Get, V2Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/roster", token: token); }
        /// <summary>Read candidates and each participant's explicit agreement or dissent.</summary>
        public Task<List<DebateProposal>> ProposalsAsync(string id, string runId, CancellationToken token = default)
        { return ReadListPagesAsync<DebateProposal>(V2Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/proposals", 50, token); }
        /// <summary>Read bounded source-linked summaries for a run.</summary>
        public Task<List<ContextSummary>> SummariesAsync(string id, string runId, int pageNumber = 1, CancellationToken token = default)
        { return SendAsync<List<ContextSummary>>(HttpMethod.Get, V2Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/summaries?pageNumber=" + pageNumber, token: token); }
        /// <summary>Read a specific summary and a bounded page of its immutable original source identities.</summary>
        public Task<List<ContextSummary>> SummaryAsync(string id, string runId, string summaryId, int sourcePageNumber = 1, CancellationToken token = default)
        { return SendAsync<List<ContextSummary>>(HttpMethod.Get, V2Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/summaries?id=" + Uri.EscapeDataString(summaryId) + "&sourcePageNumber=" + sourcePageNumber, token: token); }
        /// <summary>Read moderator clarification requests and their status.</summary>
        public Task<List<ClarificationRequest>> ClarificationsAsync(string id, string runId, CancellationToken token = default)
        { return ReadListPagesAsync<ClarificationRequest>(V2Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/clarifications", 100, token); }
        private static string V2Root(string id) { return "v2.0/api/conversations/" + Uri.EscapeDataString(id); }
        private static string Root(string id) { return "v1.0/api/conversations/" + Uri.EscapeDataString(id); }
        /// <summary>Dispose the client pool.</summary>
        public void Dispose() { _Http.Dispose(); }
    }
}
