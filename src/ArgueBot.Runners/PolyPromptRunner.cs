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
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>PolyPrompt completion adapter. No automatic tool execution, retries, or hidden history.</summary>
    public sealed class PolyPromptRunner : IRunner
    {
        /// <inheritdoc />
        public async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
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
                ToolChatRequest request = new ToolChatRequest { Messages = new List<ChatMessage> { ChatMessage.System(instructions) }, ToolChoice = "none" };
                foreach (Message message in history) request.Messages.Add(message.Speaker == "user" ? ChatMessage.User(message.Content) : ChatMessage.Assistant("[" + message.Speaker + "] " + message.Content));
                if (request.Messages.Count == 1) request.Messages.Add(ChatMessage.User("Give your opening position on the question in the instructions."));
                ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.Success) throw new DomainException(502, "provider_error", "Provider rejected the request with status " + response.StatusCode + ". Check endpoint, model and credentials.");
                int bytes = 0; bool done = false;
                await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(timeout.Token).ConfigureAwait(false))
                {
                    if (chunk.ToolCallDeltas.Count > 0) throw new DomainException(409, "tools_disabled", "This participant requested tools, but REST tool execution is disabled.");
                    if (!string.IsNullOrEmpty(chunk.Text))
                    {
                        bytes += Encoding.UTF8.GetByteCount(chunk.Text);
                        if (bytes > profile.MaxOutputBytes) throw new IOException("Provider answer exceeded the configured capture limit.");
                        await onText(chunk.Text, timeout.Token).ConfigureAwait(false);
                    }
                    if (chunk.Done) done = true;
                }
                if (!done || !response.Success) throw new IOException("Provider stream ended without a successful terminal response.");
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
