namespace ArgueBot.Runners
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Tools;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>Streaming PolyPrompt adapter with bounded, service-owned local tool continuation.</summary>
    public sealed class PolyPromptRunner : IToolRunner
    {
        private readonly bool _AllowLocalTools;
        /// <summary>Construct with the service local execution policy.</summary>
        public PolyPromptRunner(bool allowLocalTools = true) { _AllowLocalTools = allowLocalTools; }
        /// <inheritdoc />
        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { return GenerateWithToolsAsync(profile, instructions, history, onText, (_, _) => Task.CompletedTask, token); }
        /// <inheritdoc />
        public async Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        {
            string? secret = profile.SecretEnvironmentVariable.Length == 0 ? null : Environment.GetEnvironmentVariable(profile.SecretEnvironmentVariable);
            if (profile.SecretEnvironmentVariable.Length > 0 && string.IsNullOrEmpty(secret)) throw new DomainException(409, "missing_secret", "The configured credential environment reference is not set on the server.");
            using (LoggingModule logging = new LoggingModule())
            using (CompletionClientBase client = CreateClient(profile, secret, logging))
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                logging.Settings.EnableConsole = false;
                client.MaxCallDetails = 0; client.Model = profile.Model;
                timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
                string directory = profile.WorkingDirectory.Length == 0 ? Environment.CurrentDirectory : Path.GetFullPath(profile.WorkingDirectory);
                ToolChatRequest request = new ToolChatRequest { Messages = new List<ChatMessage> { ChatMessage.System(instructions) }, Tools = _AllowLocalTools ? LocalTools.Definitions() : new List<ToolDefinition>(), ToolChoice = _AllowLocalTools ? "auto" : "none" };
                foreach (Message message in history) request.Messages.Add(message.Speaker == "user" ? ChatMessage.User(message.Content) : ChatMessage.Assistant("[" + message.Speaker + "] " + message.Content));
                if (request.Messages.Count == 1) request.Messages.Add(ChatMessage.User("Give your opening position on the question in the instructions."));
                int bytes = 0; int calls = 0;
                for (int round = 0; round <= 16; round++)
                {
                    ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, timeout.Token).ConfigureAwait(false);
                    if (!response.Success) throw new DomainException(502, "provider_error", "Provider rejected the request with status " + response.StatusCode + ". Check endpoint, model and credentials.");
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
                    if (!_AllowLocalTools) throw new DomainException(403, "tools_disabled", "Local tools are disabled by the service execution setting.");
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
                            ToolExecutionResult result = await LocalTools.ExecuteAsync(call, directory, timeout.Token).ConfigureAwait(false);
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
        }
        private static CompletionClientBase CreateClient(RunnerProfile profile, string? secret, LoggingModule logging)
        {
            return profile.Provider switch
            {
                "ollama" => new OllamaCompletionClient(profile.Endpoint, secret, logging),
                "openai" => new OpenAiCompletionClient(profile.Endpoint, secret, logging),
                "anthropic" => new AnthropicCompletionClient(profile.Endpoint, secret, logging),
                "gemini" => new GeminiCompletionClient(profile.Endpoint, secret, logging),
                _ => throw new DomainException(400, "unsupported_provider", "Completion provider must be ollama, openai, anthropic or gemini.")
            };
        }
    }
}
