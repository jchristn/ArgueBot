namespace Test.Shared
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class ContextFixtureRunner : IConversationToolRunner
    {
        private readonly MultiAgentFixtureRunner _Inner = new MultiAgentFixtureRunner();
        private bool _Asked;
        internal string PrivateRecipientId { get; set; } = "";
        internal bool SkipClarification { get; set; }
        internal bool HoldCompaction { get; set; }
        internal string InvalidSummary { get; set; } = "";
        internal TaskCompletionSource<bool> CompactionStarted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> ReleaseCompaction { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string PrivateId { get; set; } = "";
        internal int Compactions { get; private set; }
        internal List<List<string>> CompactionHistories { get; } = new List<List<string>>();
        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { return _Inner.GenerateAsync(profile, instructions, history, onText, token); }
        public Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        { return GenerateAsync(profile, instructions, history, onText, token); }
        public async Task GenerateWithContextAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, bool allowLocalTools, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default)
        {
            if (history.Any(message => message.Content.Contains("PRIVATE_FIXTURE_ONLY", StringComparison.Ordinal)) && !(PrivateRecipientId.Length > 0 && (profile.Id == PrivateRecipientId || instructions.Contains("Compact the supplied", StringComparison.Ordinal) && history.All(message => message.RecipientId == PrivateRecipientId)))) throw new InvalidDataException("Private evidence leaked into another invocation.");
            if (instructions.Contains("Compact the supplied", StringComparison.Ordinal))
            {
                Compactions++; CompactionHistories.Add(history.Select(message => message.Id).ToList());
                if (HoldCompaction && Compactions == 1) { CompactionStarted.TrySetResult(true); await ReleaseCompaction.Task.WaitAsync(token).ConfigureAwait(false); }
                List<SummarySection> sections = new List<SummarySection>
                {
                    new SummarySection { Kind = "facts", Content = "No rollout outcome is established." },
                    new SummarySection { Kind = "contested_claims", Content = "Evidence remains contested." },
                    new SummarySection { Kind = "positions", Content = "No final participant agreement is established." },
                    new SummarySection { Kind = "evidence", Content = "Original evidence is retained in the source manifest." },
                    new SummarySection { Kind = "decisions", Content = "No ratified decision yet." },
                    new SummarySection { Kind = "questions", Content = "The rollout threshold remains to be clarified." },
                    new SummarySection { Kind = "constraints", Content = "Retain moderator requirements for reversible design and measured rollout." }
                };
                string pinnedList = instructions.Contains("Pinned moderator constraint source IDs: ", StringComparison.Ordinal) ? instructions.Split("Pinned moderator constraint source IDs: ")[1].Split(". ")[0] : "";
                if (pinnedList.Length > 0 && InvalidSummary != "unattributed_constraint") sections[6].Content += " Pinned: " + pinnedList + ".";
                List<string> sourceIds = history.Select(message => message.Id).ToList();
                if (Compactions > 1)
                {
                    if (InvalidSummary == "missing_section") sections.RemoveAt(6);
                    if (InvalidSummary == "duplicate_section") sections[6].Kind = "facts";
                    if (InvalidSummary == "invented_source") sourceIds.Add("msg_fictional_unprovided_source");
                    if (InvalidSummary == "missing_source") sourceIds.RemoveAt(sourceIds.Count - 1);
                }
                string overview = Compactions > 1 && InvalidSummary == "expanded_memory" ? new string('x', 12000) : (history.Any(message => message.RecipientId == PrivateRecipientId) ? "PRIVATE_FIXTURE_ONLY " : "") + "Working memory of supplied evidence.";
                await onText(JsonSerializer.Serialize(new { summary = overview, sections, sourceIds }), token).ConfigureAwait(false);
                return;
            }
            if (!SkipClarification && !_Asked && instructions.Contains("Respond to the orchestrator", StringComparison.Ordinal))
            {
                _Asked = true;
                string denied = await execute(new ConversationToolCall { Name = "get_turn", CallId = "private", ArgumentsJson = JsonSerializer.Serialize(new { id = PrivateId }) }, token).ConfigureAwait(false);
                if (!denied.Contains("not_found", StringComparison.Ordinal) || denied.Contains("PRIVATE_FIXTURE_ONLY", StringComparison.Ordinal)) throw new InvalidDataException("Retrieval exposed a private source.");
                string list = await execute(new ConversationToolCall { Name = "search_context", CallId = "search", ArgumentsJson = "{\"query\":\"PRIVATE_FIXTURE_ONLY\"}" }, token).ConfigureAwait(false);
                if (list.Contains("PRIVATE_FIXTURE_ONLY", StringComparison.Ordinal)) throw new InvalidDataException("Search exposed private evidence.");
                string response = await execute(new ConversationToolCall { Name = "request_clarification", CallId = "question", ArgumentsJson = "{\"query\":\"What is the rollout limit?\",\"blocking\":true}" }, token).ConfigureAwait(false);
                if (!response.Contains("\"success\":true", StringComparison.Ordinal)) throw new InvalidDataException("Clarification failed.");
                string retried = await execute(new ConversationToolCall { Name = "request_clarification", CallId = "question-retry", ArgumentsJson = "{\"query\":\"What is the rollout limit?\",\"blocking\":true}" }, token).ConfigureAwait(false);
                if (JsonDocument.Parse(retried).RootElement.GetProperty("result").GetProperty("id").GetString() != JsonDocument.Parse(response).RootElement.GetProperty("result").GetProperty("id").GetString()) throw new InvalidDataException("A harness retry with a new call ID created a second clarification.");
            }
            await _Inner.GenerateAsync(profile, instructions, history, onText, token).ConfigureAwait(false);
        }
    }
}
