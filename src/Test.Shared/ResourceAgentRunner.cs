namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;

    /// <summary>A tool-calling fixture: in response turns it reads the billing resource and, when writing is offered, appends a note to debate-notes. Other phases use the multi-agent fixture.</summary>
    internal sealed class ResourceAgentRunner : IConversationToolRunner
    {
        private readonly MultiAgentFixtureRunner _Inner = new MultiAgentFixtureRunner();
        internal ConcurrentQueue<string> OfferedTools { get; } = new ConcurrentQueue<string>();
        internal ConcurrentQueue<string> ResponseInstructions { get; } = new ConcurrentQueue<string>();
        internal ConcurrentQueue<string> Results { get; } = new ConcurrentQueue<string>();

        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { return _Inner.GenerateAsync(profile, instructions, history, onText, token); }

        public Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        { return _Inner.GenerateAsync(profile, instructions, history, onText, token); }

        public async Task GenerateWithContextAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, bool allowLocalTools, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        {
            bool response = !instructions.Contains("question (required", StringComparison.Ordinal) && !instructions.Contains("proposal (required", StringComparison.Ordinal) && !instructions.Contains("proposalId:", StringComparison.Ordinal) && !instructions.Contains("Compact the supplied", StringComparison.Ordinal);
            if (!response) { await _Inner.GenerateAsync(profile, instructions, history, onText, token).ConfigureAwait(false); return; }
            ResponseInstructions.Enqueue(instructions);
            OfferedTools.Enqueue(string.Join(",", tools.Select(tool => tool.Name)));
            string read = "";
            if (tools.Any(tool => tool.Name == "read_resource")) read = await CallAsync(profile, "read_resource", new { name = "billing" }, execute, onTool, token).ConfigureAwait(false);
            if (tools.Any(tool => tool.Name == "append_resource")) await CallAsync(profile, "append_resource", new { name = "debate-notes", text = "Note from " + profile.Name + "." }, execute, onTool, token).ConfigureAwait(false);
            await onText(read.Contains("Invoices go out on the 1st", StringComparison.Ordinal) ? "Billing says invoices go out on the 1st." : "No billing context was available.", token).ConfigureAwait(false);
        }

        private async Task<string> CallAsync(RunnerProfile profile, string name, object arguments, Func<ConversationToolCall, CancellationToken, Task<string>> execute, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token)
        {
            ConversationToolCall call = new ConversationToolCall { Name = name, CallId = name + "_" + profile.Id, ArgumentsJson = JsonSerializer.Serialize(arguments) };
            ToolActivity activity = new ToolActivity { CallId = call.CallId, Name = name, ArgumentsJson = call.ArgumentsJson };
            await onTool(activity, token).ConfigureAwait(false);
            string result = await execute(call, token).ConfigureAwait(false);
            activity.State = "completed"; activity.Result = result;
            await onTool(activity, token).ConfigureAwait(false);
            Results.Enqueue(name + ":" + result);
            return result;
        }
    }
}
