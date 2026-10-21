namespace ArgueBot.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Shared typed HTTP client for TUI and console surfaces. Owns its HTTP connection pool.</summary>
    public sealed class ArgueBotClient : IDisposable
    {
        private readonly HttpClient _Http;
        private readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        /// <summary>Create a client for an explicit server; loopback default is 127.0.0.1:4317. The client owns its HTTP pool and any supplied handler.</summary>
        public ArgueBotClient(string endpoint = "http://127.0.0.1:4317", HttpMessageHandler? handler = null)
        {
            Uri address = new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute);
            if ((address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) || address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0)
                throw new ArgumentException("Use an HTTP service URL without embedded credentials, query or fragment.", nameof(endpoint));
            if (!address.IsLoopback && address.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Remote service connections require HTTPS.", nameof(endpoint));
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
        public Task<Conversation> CreateConversationAsync(string title, CancellationToken token = default)
        { return SendAsync<Conversation>(HttpMethod.Post, "v1.0/api/conversations", new ConversationRequest { Title = title }, token); }
        /// <summary>Read a conversation.</summary>
        public Task<Conversation> ConversationAsync(string id, CancellationToken token = default)
        { return SendAsync<Conversation>(HttpMethod.Get, Root(id), null, token); }
        /// <summary>Update title/pin/archive using its current revision.</summary>
        public Task<Conversation> UpdateConversationAsync(Conversation conversation, CancellationToken token = default)
        { return SendAsync<Conversation>(HttpMethod.Put, Root(conversation.Id), new ConversationRequest { Title = conversation.Title, Pinned = conversation.Pinned, Archived = conversation.Archived, Revision = conversation.Revision }, token); }
        /// <summary>Read one authenticated SSE connection after a committed cursor. Frames are bounded to 1048576 UTF-16 characters, idle reads to fifteen seconds, and identifiers/order are validated. Reconnect after normal completion using the last yielded sequence; expired cursors require a fresh head followed by snapshots. Enumeration cancellation/disposal closes the HTTP body.</summary>
        public async IAsyncEnumerable<DurableEvent> WatchEventsAsync(string id, long after = 0, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (after < 0) throw new ArgumentOutOfRangeException(nameof(after));
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Root(id) + "/events"))
            {
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
        { return SendAsync<DebateRun>(HttpMethod.Post, Root(id) + "/runs", request, token); }
        /// <summary>Apply a moderator command to a run.</summary>
        public async Task ActionAsync(string id, string runId, CommandRequest request, CancellationToken token = default)
        { if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) request.IdempotencyKey = Guid.NewGuid().ToString("N"); await SendAsync<AcceptedResponse>(HttpMethod.Post, Root(id) + "/runs/" + Uri.EscapeDataString(runId) + "/actions", request, token).ConfigureAwait(false); }
        /// <summary>Read a typed API response and normalize safe domain failures.</summary>
        public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken token = default)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? absolute) || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\'))
                throw new ArgumentException("API paths must be relative to the configured service.", nameof(path));
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                if (body != null) request.Content = JsonContent.Create(body, options: _Json);
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new DomainException((int)response.StatusCode, "redirect_refused", "Configure the final service URL explicitly; API redirects require a direct endpoint.");
                        ApiError? error = await response.Content.ReadFromJsonAsync<ApiError>(_Json, token).ConfigureAwait(false);
                        throw new DomainException((int)response.StatusCode, error?.Code ?? "http_error", error?.Message ?? response.ReasonPhrase ?? "Request failed.");
                    }
                    return await response.Content.ReadFromJsonAsync<T>(_Json, token).ConfigureAwait(false) ?? throw new InvalidDataException("Server returned an empty response.");
                }
            }
        }
        private static string Root(string id) { return "v1.0/api/conversations/" + Uri.EscapeDataString(id); }
        /// <summary>Dispose the client pool.</summary>
        public void Dispose() { _Http.Dispose(); }
    }
}
