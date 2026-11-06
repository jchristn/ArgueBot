namespace ArgueBot.Runners
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Tools;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Streaming PolyPrompt adapter with bounded, service-owned local tool continuation.</summary>
    public sealed class PolyPromptRunner : IToolRunner
    {
        private readonly bool _AllowLocalTools;
        private readonly IReadOnlyList<ConversationToolDefinition> _ContextTools;
        private readonly Func<ConversationToolCall, CancellationToken, Task<string>>? _ExecuteContext;
        internal PolyPromptRunner(bool allowLocalTools, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute)
        { _AllowLocalTools = allowLocalTools; _ContextTools = tools; _ExecuteContext = execute; }
        /// <summary>Construct with the service local execution policy.</summary>
        public PolyPromptRunner(bool allowLocalTools = true) { _AllowLocalTools = allowLocalTools; _ContextTools = new List<ConversationToolDefinition>(); }
        /// <inheritdoc />
        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { return GenerateWithToolsAsync(profile, instructions, history, onText, (_, _) => Task.CompletedTask, token); }
        /// <inheritdoc />
        public async Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        {
            string? secret = profile.SecretEnvironmentVariable.Length == 0 ? null : Environment.GetEnvironmentVariable(profile.SecretEnvironmentVariable);
            if (profile.SecretEnvironmentVariable.Length > 0 && string.IsNullOrEmpty(secret)) throw new DomainException(409, "missing_secret", "The configured credential environment reference is not set on the server.");
            EndpointAuthHandler auth = new EndpointAuthHandler(profile);
            using (HttpClient http = new HttpClient(auth))
            using (LoggingModule logging = new LoggingModule())
            using (CompletionClientBase client = CreateClient(profile, secret, logging, http))
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                logging.Settings.EnableConsole = false;
                client.MaxCallDetails = 0; client.Model = profile.Model;
                timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
                try
                {
                    string directory = profile.WorkingDirectory.Length == 0 ? Environment.CurrentDirectory : Path.GetFullPath(profile.WorkingDirectory);
                    ToolChatRequest request = BuildRequest(profile, instructions, history, _ContextTools, _AllowLocalTools);
                    int bytes = 0; int calls = 0;
                    for (int round = 0; round <= 16; round++)
                    {
                        if (JsonSerializer.Serialize(request, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Length > profile.ContextMaxCharacters) throw new DomainException(409, "context_budget_exceeded", "The request and tool continuation exceed the model context character budget. Originals and tool history are retained.");
                        ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, timeout.Token).ConfigureAwait(false);
                        if (!response.Success) throw ProviderFailure(response.StatusCode ?? 0, auth.RetryAfterSeconds);
                        bool done = false; bool roundHasText = false; int argumentBytes = 0;
                        await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(timeout.Token).ConfigureAwait(false))
                        {
                            foreach (ToolCallDelta delta in chunk.ToolCallDeltas)
                            {
                                argumentBytes += Encoding.UTF8.GetByteCount(delta.ArgumentsJsonDelta ?? delta.ArgumentsJson ?? "") + Encoding.UTF8.GetByteCount(delta.Name ?? "");
                                if (argumentBytes > 131072) throw new DomainException(502, "tool_arguments_too_large", "Model tool requests exceeded 128 KiB. No tools from this response were executed.");
                            }
                            if (!string.IsNullOrEmpty(chunk.Text))
                            {
                                if (!roundHasText && bytes > 0)
                                {
                                    bytes += 2;
                                    await onText("\n\n", timeout.Token).ConfigureAwait(false);
                                }
                                roundHasText = true;
                                bytes += Encoding.UTF8.GetByteCount(chunk.Text);
                                if (bytes > profile.MaxOutputBytes) throw new IOException("Provider answer exceeded the configured capture limit.");
                                await onText(chunk.Text, timeout.Token).ConfigureAwait(false);
                            }
                            if (chunk.Done) done = true;
                        }
                        if (!done || !response.Success) throw new IOException("Provider stream ended without a successful terminal response. Tool calls from an incomplete response were not executed.");
                        if (response.ToolCalls.Count == 0)
                        {
                            if (string.IsNullOrWhiteSpace(response.Text)) throw new DomainException(502, "empty_answer", "The model returned no final answer after completion. Inspect its tool activity and provider settings.");
                            return;
                        }
                        if (!_AllowLocalTools && _ContextTools.Count == 0) throw new DomainException(403, "tools_disabled", "Local tools are disabled by the service execution setting.");
                        if (round == 16) throw new DomainException(409, "tool_limit", "The model reached the 16 tool continuation rounds per turn limit.");
                        if (calls + response.ToolCalls.Count > 32) throw new DomainException(409, "tool_limit", "The model reached the 32 local tool calls per turn limit.");
                        request.Messages.Add(response.ToAssistantMessage());
                        HashSet<string> invocationIds = new HashSet<string>(StringComparer.Ordinal);
                        foreach (ToolCall call in response.ToolCalls)
                        {
                            calls++;
                            string id = string.IsNullOrEmpty(call.Id) ? "local_" + calls : call.Id;
                            if (!invocationIds.Add(id)) throw new DomainException(502, "duplicate_tool_call", "The provider repeated a tool call identifier. The operation was not executed again.");
                            ToolActivity activity = new ToolActivity { CallId = "round_" + round + ":" + id, Name = call.Name, ArgumentsJson = call.ArgumentsJson };
                            await onTool(activity, timeout.Token).ConfigureAwait(false);
                            try
                            {
                                ToolExecutionResult result;
                                if (_ExecuteContext != null && _ContextTools.Any(definition => definition.Name == call.Name))
                                    result = new ToolExecutionResult { Success = true, Content = await _ExecuteContext(new ConversationToolCall { CallId = activity.CallId, Name = call.Name, ArgumentsJson = call.ArgumentsJson }, timeout.Token).ConfigureAwait(false) };
                                else if (_AllowLocalTools) result = await LocalTools.ExecuteAsync(call, directory, timeout.Token).ConfigureAwait(false);
                                else result = new ToolExecutionResult { Success = false, Content = "{\"success\":false,\"error\":\"Only advertised conversation tools are enabled.\"}" };
                                activity.State = result.Success ? "completed" : "failed"; activity.Result = result.Content;
                                await onTool(activity, timeout.Token).ConfigureAwait(false);
                                request.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, result.Content));
                            }
                            catch (OperationCanceledException)
                            {
                                activity.State = "interrupted"; activity.Result = "Tool execution was stopped. An interrupted file write or external command may have made changes; inspect before retrying.";
                                await onTool(activity, CancellationToken.None).ConfigureAwait(false);
                                throw;
                            }
                        }
                    }
                    throw new DomainException(409, "tool_limit", "The model reached the 16 tool continuation rounds per turn limit.");
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    throw new DomainException(504, "generation_timeout", "The agent exceeded the configured generation deadline.");
                }
            }
        }
        internal static long MeasureContext(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, bool allowLocalTools)
        { return JsonSerializer.Serialize(BuildRequest(profile, instructions, history, tools, allowLocalTools), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Length; }
        /// <summary>Classify a provider HTTP refusal so the moderator sees the cause and the right recovery.</summary>
        internal static DomainException ProviderFailure(int status, int? retryAfterSeconds)
        {
            if (status == 401 || status == 403) return new DomainException(502, "provider_unauthorized", "The provider refused the credentials with status " + status + ". Check the credential or API key bound to this endpoint, and that it may use this model, then resume.");
            if (status == 429) return new DomainException(502, "provider_rate_limited", "The provider is rate limiting requests (status 429)." + (retryAfterSeconds.HasValue ? " It asked to wait " + retryAfterSeconds.Value + (retryAfterSeconds.Value == 1 ? " second" : " seconds") + " before retrying." : " Wait before retrying.") + " A debate retries automatically within its step retry limit; resume after that if it paused.");
            if (status >= 500) return new DomainException(502, "provider_unavailable", "The provider failed with status " + status + ". It may be overloaded or restarting. A debate retries automatically within its step retry limit; resume later if it paused.");
            return new DomainException(502, "provider_error", "Provider rejected the request with status " + status + ". Check endpoint, model and credentials.");
        }
        private static ToolChatRequest BuildRequest(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, bool allowLocalTools)
        {
            ToolChatRequest request = new ToolChatRequest { Messages = new List<ChatMessage> { ChatMessage.System(instructions) }, Tools = allowLocalTools && profile.SupportsTools ? LocalTools.Definitions() : new List<ToolDefinition>(), ToolChoice = allowLocalTools ? "auto" : "none" };
            request.Options = new CompletionOptions { MaxTokens = profile.Generation.MaxOutputTokens, Temperature = profile.Generation.Temperature, TopP = profile.Generation.TopP };
            foreach (ConversationToolDefinition definition in tools.Where(_ => profile.SupportsTools)) request.Tools.Add(ToolDefinition.Function(definition.Name, definition.Description, JsonSerializer.Deserialize<Dictionary<string, object>>(definition.Schema)!));
            if (request.Tools.Count > 0) request.ToolChoice = "auto";
            foreach (Message message in history) request.Messages.Add(message.Speaker == "user" ? ChatMessage.User(message.Content) : ChatMessage.Assistant("[" + message.Speaker + "] " + message.Content));
            if (request.Messages.Count == 1) request.Messages.Add(ChatMessage.User("Give your opening position on the question in the instructions."));
            return request;
        }
        private static CompletionClientBase CreateClient(RunnerProfile profile, string? secret, LoggingModule logging, HttpClient http)
        {
            return profile.Provider switch
            {
                "ollama" => new OllamaCompletionClient(profile.Endpoint, secret, logging, http),
                "openai" => new OpenAiCompletionClient(profile.Endpoint, secret, logging, http),
                "anthropic" => new AnthropicCompletionClient(profile.Endpoint, secret, logging, http),
                "gemini" => new GeminiCompletionClient(profile.Endpoint, secret, logging, http),
                _ => throw new DomainException(400, "unsupported_provider", "Completion provider must be ollama, openai, anthropic or gemini.")
            };
        }
    }
}
