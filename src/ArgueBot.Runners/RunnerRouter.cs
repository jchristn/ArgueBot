namespace ArgueBot.Runners
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Observability;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Processes;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Selects the configured runner without moving execution into a UI.</summary>
    public sealed class RunnerRouter : IConversationToolRunner, IContextSizeEstimator
    {
        private readonly bool _AllowCli;
        /// <summary>Construct with the host's CLI execution policy.</summary>
        public RunnerRouter(bool allowCli) { _AllowCli = allowCli; }
        /// <inheritdoc />
        public long MeasureContext(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, bool allowLocalTools)
        {
            if (profile.Kind == "rest") return PolyPromptRunner.MeasureContext(profile, instructions, history, tools, _AllowCli && allowLocalTools);
            long characters = instructions.Length + 1024;
            foreach (Message message in history) characters += message.Content.Length + message.Speaker.Length + 32;
            foreach (ConversationToolDefinition tool in tools) characters += tool.Schema.Length + tool.Description.Length + tool.Name.Length + 128;
            return characters;
        }
        /// <inheritdoc />
        public async Task GenerateWithContextAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, bool allowLocalTools, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        {
            if (profile.Kind == "cli")
            {
                if (!_AllowCli) throw new DomainException(403, "cli_disabled", "CLI harness execution is disabled.");
                if (profile.RuntimeMcpCommand.Count > 0 && (profile.Provider == "claude" || profile.Provider == "codex"))
                    await Mcp.McpInvocationRunner.GenerateAsync(profile, instructions, history, tools, execute, onText, onTool, token).ConfigureAwait(false);
                else await ContextEnvelopeRunner.GenerateAsync(profile, instructions, history, tools, execute, onText, onTool, token).ConfigureAwait(false);
            }
            else await new PolyPromptRunner(_AllowCli && allowLocalTools, tools, execute).GenerateWithToolsAsync(profile, instructions, history, onText, onTool, token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { return GenerateWithToolsAsync(profile, instructions, history, onText, (_, _) => Task.CompletedTask, token); }
        /// <inheritdoc />
        public async Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        {
            if (profile.Kind == "cli" && !_AllowCli) throw new DomainException(403, "cli_disabled", "Shell-out execution is disabled on this server.");
            IRunner runner = profile.Kind == "cli" ? new CliRunner() : new PolyPromptRunner(_AllowCli);
            long started = Stopwatch.GetTimestamp(); string outcome = "failed"; bool first = true;
            using (Activity? activity = ProductTelemetry.Activities.StartActivity("runner.generate"))
            {
                activity?.SetTag("runner.kind", profile.Kind); activity?.SetTag("runner.provider", profile.Provider);
                try
                {
                    Func<string, CancellationToken, Task> forward = async (delta, cancellation) =>
                    {
                        if (first && delta.Length > 0) { first = false; ProductTelemetry.FirstTextDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("runner", profile.Kind)); }
                        await onText(delta, cancellation).ConfigureAwait(false);
                    };
                    if (runner is IToolRunner toolRunner) await toolRunner.GenerateWithToolsAsync(profile, instructions, history, forward, onTool, token).ConfigureAwait(false);
                    else await runner.GenerateAsync(profile, instructions, history, forward, token).ConfigureAwait(false);
                    outcome = "success";
                }
                catch (OperationCanceledException) { outcome = "cancelled"; activity?.SetStatus(ActivityStatusCode.Error); throw; }
                catch { activity?.SetStatus(ActivityStatusCode.Error); throw; }
                finally { TagList tags = new TagList { { "runner", profile.Kind }, { "outcome", outcome } }; ProductTelemetry.Generations.Add(1, tags); ProductTelemetry.GenerationDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags); }
            }
        }
    }
}
