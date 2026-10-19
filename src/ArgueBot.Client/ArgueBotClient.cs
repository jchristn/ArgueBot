namespace ArgueBot.Client
{
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Shared typed HTTP client for TUI and console surfaces. Owns its HTTP connection pool.</summary>
    public sealed class ArgueBotClient : IDisposable
    {
        private readonly HttpClient _Http;
        private readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        /// <summary>Create a client for an explicit server; loopback default is 127.0.0.1:4317.</summary>
        public ArgueBotClient(string endpoint = "http://127.0.0.1:4317")
        {
            Uri address = new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute);
            if ((address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) || address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0)
                throw new ArgumentException("Use an HTTP service URL without embedded credentials, query or fragment.", nameof(endpoint));
            if (!address.IsLoopback && address.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Remote service connections require HTTPS.", nameof(endpoint));
            _Http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
            _Http.DefaultRequestHeaders.Add("x-client-id", Guid.NewGuid().ToString("N"));
        }
        /// <summary>Use an existing session. Secrets are never included in URLs.</summary>
        public void SetToken(string token) { _Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); }
        /// <summary>Pair using the owner-readable code file; never use this method for remote server enrollment.</summary>
        public async Task PairLocalAsync(string home, CancellationToken token = default)
        {
            if (_Http.BaseAddress == null || !_Http.BaseAddress.IsLoopback) throw new InvalidOperationException("Local code enrollment requires a loopback server.");
            for (int attempt = 0; ; attempt++)
            {
                string code = await File.ReadAllTextAsync(Path.Combine(home, "runtime", "pairing-code"), token).ConfigureAwait(false);
                try
                {
                    EnrollmentResponse response = await SendAsync<EnrollmentResponse>(HttpMethod.Post, "v1.0/api/auth/pair", new EnrollmentRequest { Code = code.Trim() }, token).ConfigureAwait(false);
                    SetToken(response.Token); return;
                }
                catch (DomainException ex) when (ex.Code == "invalid_pairing" && attempt < 2)
                {
                    // Another local client may have exchanged the same single-use code first.
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
            }
        }
        /// <summary>Download a ready archive to a new owner-readable file. Failed or cancelled transfers never publish a partial destination.</summary>
        public async Task DownloadBackupAsync(string id, string destination, CancellationToken token = default)
        {
            string target = Path.GetFullPath(destination);
            if (File.Exists(target)) throw new IOException("The download destination already exists.");
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (HttpResponseMessage response = await _Http.GetAsync("v1.0/api/backups/" + Uri.EscapeDataString(id) + "/download", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new DomainException((int)response.StatusCode, "download_failed", "The archive could not be downloaded.");
                    using (Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        ArgueBot.Core.Configuration.HomePermissions.File(temporary);
                        await input.CopyToAsync(output, token).ConfigureAwait(false);
                        await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
                    }
                }
                File.Move(temporary, target, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
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
        /// <summary>Read ordered history.</summary>
        public Task<List<Message>> MessagesAsync(string id, CancellationToken token = default)
        { return SendAsync<List<Message>>(HttpMethod.Get, Root(id) + "/messages", null, token); }
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
