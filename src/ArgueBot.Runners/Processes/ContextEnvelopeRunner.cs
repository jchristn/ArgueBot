namespace ArgueBot.Runners.Processes
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class ContextEnvelopeRunner
    {
        internal static async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token)
        {
            string prompt = instructions + "\nYou may retrieve conversation evidence before answering. To request a tool, return ONLY a JSON object with contextTool: {name: tool name, argumentsJson: a JSON string containing its arguments}. The service will invoke it and return its result before your next response. Do not run shell commands to access the service. Available tools: " + JsonSerializer.Serialize(tools, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            List<Message> continuation = new List<Message>(history);
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
                try
                {
                    for (int round = 0; round <= 16; round++)
                    {
                        long characters = prompt.Length;
                        foreach (Message message in continuation) characters += message.Content.Length + message.Speaker.Length;
                        if (characters > profile.ContextMaxCharacters) throw new DomainException(409, "context_budget_exceeded", "The CLI tool continuation exceeds the configured context character budget. Original evidence remains available.");
                        StringBuilder output = new StringBuilder();
                        await new CliRunner().GenerateAsync(profile, prompt, continuation, (delta, cancellation) => { cancellation.ThrowIfCancellationRequested(); output.Append(delta); return Task.CompletedTask; }, deadline.Token).ConfigureAwait(false);
                        string answer = output.ToString(); ContextToolEnvelope? envelope = null;
                        try { envelope = JsonSerializer.Deserialize<ContextToolEnvelope>(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
                        catch (JsonException) { }
                        if (envelope?.ContextTool == null) { await onText(answer, deadline.Token).ConfigureAwait(false); return; }
                        if (round == 16) throw new DomainException(409, "tool_limit", "Conversation tools reached the sixteen continuation limit.");
                        ConversationToolCall call = envelope.ContextTool; call.CallId = "context_" + round;
                        ToolActivity activity = new ToolActivity { CallId = call.CallId, Name = call.Name, ArgumentsJson = call.ArgumentsJson };
                        await onTool(activity, deadline.Token).ConfigureAwait(false);
                        string result = await execute(call, deadline.Token).ConfigureAwait(false);
                        activity.State = "completed"; activity.Result = result;
                        await onTool(activity, deadline.Token).ConfigureAwait(false);
                        continuation.Add(new Message { Speaker = "ArgueBot tool", Kind = "tool", Content = call.Name + ": " + result });
                        if (continuation.Count > history.Count + 16) throw new DomainException(409, "tool_limit", "Conversation tool context reached its limit.");
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
                { throw new DomainException(504, "generation_timeout", "The invocation exceeded its deadline, including conversation tools."); }
            }
        }
    }
}
