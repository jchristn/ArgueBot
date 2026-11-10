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

    /// <summary>A panel fixture whose named seat asks the user one question during its response, then answers from the tool result or, on resume, from the answer in history.</summary>
    internal sealed class QuestionFixtureRunner : IConversationToolRunner
    {
        private readonly MultiAgentFixtureRunner _Inner = new MultiAgentFixtureRunner();
        private int _Asked;

        /// <summary>Seat whose response asks; the others answer normally.</summary>
        internal string AskerName { get; set; } = "Question agent 1";

        /// <summary>Arguments the asking seat sends to ask_user.</summary>
        internal string Arguments { get; set; } = JsonSerializer.Serialize(new { context = "The scope decides the design.", questions = new object[] { new { key = "scope", header = "Scope", prompt = "Which environments?", kind = "single_choice", options = new object[] { new { id = "prod", label = "Production only", recommended = true }, new { id = "all", label = "Every environment" } }, allowOther = true } } });

        /// <summary>Ask from the orchestrator's intake step instead of a participant's response.</summary>
        internal bool AskDuringIntake { get; set; }

        /// <summary>Intake invocations seen.</summary>
        internal int Intakes { get; private set; }

        /// <summary>Tool results the asking seat received, in order.</summary>
        internal ConcurrentQueue<string> Results { get; } = new ConcurrentQueue<string>();

        /// <summary>Completed when the asking seat has sent its question.</summary>
        internal TaskCompletionSource<bool> AskSent { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Prompts each invocation received, for assertions on resume instructions.</summary>
        internal ConcurrentQueue<string> Prompts { get; } = new ConcurrentQueue<string>();

        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { return _Inner.GenerateAsync(profile, instructions, history, onText, token); }

        public Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        { return GenerateAsync(profile, instructions, history, onText, token); }

        public async Task GenerateWithContextAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, bool allowLocalTools, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        {
            Prompts.Enqueue(instructions);
            bool intake = instructions.Contains("Before the panel starts", StringComparison.Ordinal);
            if (intake) Intakes++;
            bool asker = profile.Name == AskerName && (AskDuringIntake ? intake : instructions.Contains("Respond to the orchestrator", StringComparison.Ordinal));
            string? answered = history.LastOrDefault(message => message.Kind == "answer")?.Content;
            if (intake && (!asker || answered != null))
            {
                await onText(answered == null ? "The question is clear; the panel can start." : "Restated with the user's constraints.", token).ConfigureAwait(false);
                return;
            }
            if (asker && answered != null && instructions.Contains("You paused this step to ask the user", StringComparison.Ordinal))
            {
                await onText("Resumed with: " + answered.Split('\n').Last().Trim() + "\n", token).ConfigureAwait(false);
            }
            else if (asker && Interlocked.CompareExchange(ref _Asked, 1, 0) == 0)
            {
                if (!tools.Any(tool => tool.Name == "ask_user")) throw new InvalidOperationException("ask_user was not offered to the asking seat.");
                Task<string> pending = execute(new ConversationToolCall { Name = "ask_user", CallId = "ask", ArgumentsJson = Arguments }, token);
                AskSent.TrySetResult(true);
                string result = await pending.ConfigureAwait(false);
                Results.Enqueue(result);
                using (JsonDocument document = JsonDocument.Parse(result))
                {
                    JsonElement body = document.RootElement.GetProperty("result");
                    string status = body.GetProperty("status").GetString() ?? "";
                    if (status == "parked") { await onText("Waiting for the scope.", token).ConfigureAwait(false); return; }
                    if (status == "cancelled") { await onText("Stopped while waiting.", token).ConfigureAwait(false); return; }
                    string answer = status == "answered" ? string.Join(", ", body.GetProperty("answers")[0].GetProperty("selectedLabels").EnumerateArray().Select(item => item.GetString())) : "assumed production";
                    await onText("Held answer: " + (answer.Length == 0 ? "other" : answer) + "\n", token).ConfigureAwait(false);
                    if (intake) return;
                }
            }
            await _Inner.GenerateAsync(profile, instructions, history, onText, token).ConfigureAwait(false);
        }
    }
}
