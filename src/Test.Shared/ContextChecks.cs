namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class ContextChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-context-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    await RunInDatabaseAsync(database, context, home, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        internal static async Task RunInDatabaseAsync(DatabaseDriverBase database, RequestContext context, string home, CancellationToken token)
        {
            await PrivatePartitionAsync(database, context, token).ConfigureAwait(false);
            await InvalidSummariesAsync(database, context, token).ConfigureAwait(false);
            List<string> profiles = new List<string>();
            for (int index = 0; index < 3; index++) profiles.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Agent " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);
            Conversation conversation = await database.CreateConversationAsync(context, "Context evidence", token).ConfigureAwait(false);
            InterjectionReceipt early = await database.Interjections.CreateAsync(context, conversation.Id, new InterjectionRequest { Text = "Keep the minority objection visible.", DeliveryMode = "next_turn", IdempotencyKey = "early-context" }, token).ConfigureAwait(false);
            List<string> originals = new List<string>();
            for (int index = 0; index < 10; index++) originals.Add((await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "Evidence " + index + ": " + new string('x', 1500), Kind = "note" }, "seed-" + index, token).ConfigureAwait(false)).Id);
            Message privateTurn = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "PRIVATE_FIXTURE_ONLY", RecipientId = profiles[2], Kind = "steer" }, "private", token).ConfigureAwait(false);
            ContextFixtureRunner runner = new ContextFixtureRunner { PrivateId = privateTurn.Id };
            await using (ConversationEngine engine = new ConversationEngine(database, runner, contextMaxCharacters: 12000, contextMaxMessages: 20))
            {
                DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[2], ParticipantIds = profiles.Take(2).ToList(), MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "start" }, token).ConfigureAwait(false);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                Require(run.Outcome == "awaiting_clarification", "A blocking question must pause: " + run.Outcome);
                Require(run.Phase == "response" && run.SpeakerIndex == 0, "The turn that asked a blocking question must not advance before the moderator answers: " + run.Phase + "/" + run.SpeakerIndex);
                Require(runner.Compactions >= 2, "Large history must compact in bounded chunks.");
                List<ContextSummary> summaries = await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                ContextSummary current = summaries.Single(item => item.Active);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, conversation.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                GenerationAttempt response = attempts.Items.First(item => item.Purpose == "response");
                Require(originals.All(id => current.SourceIds.Contains(id) || response.ContextMessageIds.Contains(id)), "Every original must remain represented by summary provenance or exact selected context.");
                Require(current.SourceIds.Count >= 5, "Repeated compaction must expand prior summaries into original sources.");
                Require(current.ParentSummaryIds.Count == 1 && summaries.Any(item => item.Id == current.ParentSummaryIds[0] && !item.Active), "Repeated compaction must expose its immutable parent summary.");
                Require(current.Sections.Count == 7 && current.Sections.Select(section => section.Kind).Distinct().Count() == 7 && current.Content.Contains("## Moderator constraints", StringComparison.Ordinal), "Structured summary categories were not persisted and rendered.");
                await VerifyHashesAsync(database, context, conversation.Id, current, token).ConfigureAwait(false);
                Require(!current.SourceIds.Contains(privateTurn.Id), "Private turns must not enter public summary sources.");
                foreach (string id in originals) Require((await database.ReadMessageAsync(context, conversation.Id, id, token).ConfigureAwait(false)).Content.Length > 1500, "Original sources must remain intact.");
                try { await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "premature" }, "fixture", token).ConfigureAwait(false); throw new InvalidDataException("An unanswered blocking question was ignored."); }
                catch (DomainException error) when (error.Code == "clarification_required") { }
                ClarificationRequest question = (await database.Clarifications.EnumerateAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false)).Single();
                ConversationTools pagingTools = new ConversationTools(database, context, run, run.Participants[0].Id, long.MaxValue, "pagination-fixture");
                Message unicode = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "🧪 Retain complete Unicode evidence." }, token: token).ConfigureAwait(false);
                string listed = await pagingTools.ExecuteAsync(new ConversationToolCall { Name = "list_turns", CallId = "list-private", ArgumentsJson = "{\"after\":0,\"limit\":20}" }, token).ConfigureAwait(false);
                Require(!listed.Contains(privateTurn.Id, StringComparison.Ordinal) && !listed.Contains("PRIVATE_FIXTURE_ONLY", StringComparison.Ordinal), "list_turns exposed another seat's private directive.");
                string linked = await pagingTools.ExecuteAsync(new ConversationToolCall { Name = "get_supporting_details", CallId = "summary-links", ArgumentsJson = JsonSerializer.Serialize(new { ids = new[] { current.MessageId } }) }, token).ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(linked))
                {
                    List<string> links = document.RootElement.GetProperty("result").GetProperty("evidence")[0].GetProperty("linkedSourceIds").EnumerateArray().Select(item => item.GetString()!).ToList();
                    Require(links.Count > 0 && links.All(current.SourceIds.Contains) && !links.Contains(privateTurn.Id), "Supporting details for a summary must link its visible original sources: " + linked);
                }
                string split = await pagingTools.ExecuteAsync(new ConversationToolCall { Name = "get_turn", CallId = "split-unicode", ArgumentsJson = JsonSerializer.Serialize(new { id = unicode.Id, length = 1 }) }, token).ConfigureAwait(false);
                Require(split.Contains("invalid_length", StringComparison.Ordinal), "A one-unit Unicode excerpt returned a nonadvancing cursor.");
                string whole = await pagingTools.ExecuteAsync(new ConversationToolCall { Name = "get_turn", CallId = "whole-unicode", ArgumentsJson = JsonSerializer.Serialize(new { id = unicode.Id, length = 2 }) }, token).ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(whole)) Require(document.RootElement.GetProperty("result").GetProperty("nextOffset").GetInt32() == 2, "A complete Unicode excerpt did not advance correctly.");
                for (int index = 0; index < 101; index++) await pagingTools.ExecuteAsync(new ConversationToolCall { Name = "request_clarification", CallId = "optional-" + index, ArgumentsJson = "{\"query\":\"Optional historical question " + index + "\",\"blocking\":false}" }, token).ConfigureAwait(false);
                List<ClarificationRequest> firstPage = await database.Clarifications.EnumerateAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                List<ClarificationRequest> secondPage = await database.Clarifications.EnumerateAsync(context, conversation.Id, run.Id, token, pageNumber: 2).ConfigureAwait(false);
                Require(firstPage.Count == 100 && secondPage.Count == 2 && secondPage.Any(item => item.Id == question.Id), "Clarification history must retain older questions beyond the first page.");
                Require(await database.Clarifications.HasBlockingAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false), "Display paging must not hide a blocking clarification from resume validation.");
                await engine.InterjectAsync(context, conversation.Id, new InterjectionRequest { RunId = run.Id, Text = "Roll out to five percent first.", ClarificationId = question.Id, Pinned = true, DeliveryMode = "next_turn", IdempotencyKey = "answer" }, token).ConfigureAwait(false);
                await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "resume" }, "fixture", token).ConfigureAwait(false);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                Require(run.Outcome == "consensus", "Answered clarification must permit fresh unanimous ratification: " + run.Outcome);
                List<InterjectionReceipt> delivered = await database.Interjections.EnumerateAsync(context, conversation.Id, token: token).ConfigureAwait(false);
                InterjectionReceipt summarized = delivered.Single(receipt => receipt.Id == early.Id);
                Require(summarized.DeliveredParticipantIds.Count == 0 && summarized.SummarizedParticipantIds.Count == 2 && summarized.Status == "summarized", "Derived memory must not be reported as direct delivery of original context.");
                Require(delivered.Single(receipt => receipt.Pinned).DeliveredParticipantIds.Count == 2, "Pinned original context must remain directly selected despite compaction.");
            }
        }
        private static async Task PrivatePartitionAsync(DatabaseDriverBase database, RequestContext context, CancellationToken token)
        {
            List<string> profiles = new List<string>();
            for (int index = 0; index < 3; index++) profiles.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Private partition " + index, Executable = "fictional" }, token).ConfigureAwait(false)).Id);
            Conversation conversation = await database.CreateConversationAsync(context, "Private context compaction", token).ConfigureAwait(false);
            CreateRunRequest request = new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[2], ParticipantIds = profiles.Take(2).ToList(), MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "private-partition" };
            DebateRun run = await database.Runs.CreateAsync(context, conversation.Id, request, token).ConfigureAwait(false);
            string recipient = run.Participants[0].Id;
            List<string> originals = new List<string>();
            for (int index = 0; index < 125; index++) originals.Add((await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, RunId = run.Id, RecipientId = recipient, Kind = "tool", Content = "PRIVATE_FIXTURE_ONLY evidence " + index + new string('x', 150) }, token: token).ConfigureAwait(false)).Id);
            ContextFixtureRunner runner = new ContextFixtureRunner { PrivateRecipientId = recipient, SkipClarification = true };
            await using (ConversationEngine engine = new ConversationEngine(database, runner, contextMaxCharacters: 12000, contextMaxMessages: 20))
            {
                await engine.StartAsync(context, conversation.Id, request, token).ConfigureAwait(false);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                Require(run.Outcome == "consensus", "Private compaction did not finish: " + run.Outcome);
                List<ContextSummary> summaries = await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                ContextSummary current = summaries.Single(item => item.Active && item.RecipientId == recipient);
                Require(current.SourceCount > 100 && current.SourceIds.Count == 100, "Source manifests must expose totals and bounded first pages.");
                ContextSummary second = (await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token, id: current.Id, sourcePageNumber: 2).ConfigureAwait(false)).Single();
                Require(second.SourceIds.Count == current.SourceCount - 100 && !second.SourceIds.Intersect(current.SourceIds).Any(), "Source paging lost or duplicated evidence.");
                await VerifyHashesAsync(database, context, conversation.Id, second, token).ConfigureAwait(false);
                foreach (RunnerProfile profile in run.Participants.Concat(new[] { run.Orchestrator! }))
                {
                    ConversationTools tools = new ConversationTools(database, context, run, profile.Id, long.MaxValue, "private-tool-" + profile.Id);
                    string result = await tools.ExecuteAsync(new ConversationToolCall { Name = "get_summary", CallId = "private-summary", ArgumentsJson = System.Text.Json.JsonSerializer.Serialize(new { id = current.Id, offset = 100 }) }, token).ConfigureAwait(false);
                    Require(result.Contains("PRIVATE_FIXTURE_ONLY", StringComparison.Ordinal) == (profile.Id == recipient), "Private memory retrieval widened visibility.");
                    if (profile.Id == recipient) Require(second.SourceIds.All(id => result.Contains(id, StringComparison.Ordinal)), "Model retrieval omitted later source identities.");
                    if (profile.Id == recipient)
                    {
                        StringBuilder reconstructed = new StringBuilder(); int offset = 0;
                        do
                        {
                            string excerpt = await tools.ExecuteAsync(new ConversationToolCall { Name = "get_summary", CallId = "summary-excerpt-" + offset, ArgumentsJson = JsonSerializer.Serialize(new { id = current.Id, contentOffset = offset, length = 64, offset = 100 }) }, token).ConfigureAwait(false);
                            using (JsonDocument document = JsonDocument.Parse(excerpt))
                            {
                                JsonElement item = document.RootElement.GetProperty("result")[0];
                                reconstructed.Append(item.GetProperty("content").GetString());
                                JsonElement next = item.GetProperty("nextContentOffset");
                                offset = next.ValueKind == JsonValueKind.Null ? -1 : next.GetInt32();
                            }
                        } while (offset >= 0);
                        Require(reconstructed.ToString() == current.Content, "Summary excerpt paging lost or repeated text.");
                        string invalid = await tools.ExecuteAsync(new ConversationToolCall { Name = "get_summary", CallId = "summary-invalid-cursor", ArgumentsJson = "{\"offset\":1}" }, token).ConfigureAwait(false);
                        Require(invalid.Contains("invalid_tool_arguments", StringComparison.Ordinal), "A partial summary source-page cursor was silently rounded.");
                    }
                }
                foreach (string id in originals) Require((await database.ReadMessageAsync(context, conversation.Id, id, token).ConfigureAwait(false)).Content.Contains("PRIVATE_FIXTURE_ONLY", StringComparison.Ordinal), "Private compaction changed original evidence.");
            }
        }
        private static async Task InvalidSummariesAsync(DatabaseDriverBase database, RequestContext context, CancellationToken token)
        {
            List<string> profiles = new List<string>();
            for (int index = 0; index < 3; index++) profiles.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Summary validation " + index, Executable = "fictional" }, token).ConfigureAwait(false)).Id);
            foreach (int sourceCount in new[] { 1, 3 })
            {
                Conversation pinned = await database.CreateConversationAsync(context, "Pinned context exceeds capacity", token).ConfigureAwait(false);
                List<InterjectionReceipt> originals = new List<InterjectionReceipt>();
                for (int index = 0; index < sourceCount; index++) originals.Add(await database.Interjections.CreateAsync(context, pinned.Id, new InterjectionRequest { Text = "Pinned original " + index + ": " + new string('x', sourceCount == 1 ? 18000 : 5000), Pinned = true, DeliveryMode = "next_turn", IdempotencyKey = "pin-" + index }, token).ConfigureAwait(false));
                ContextFixtureRunner runner = new ContextFixtureRunner { SkipClarification = true };
                await using (ConversationEngine engine = new ConversationEngine(database, runner, contextMaxCharacters: 12000, contextMaxMessages: 20))
                {
                    DebateRun run = await engine.StartAsync(context, pinned.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[2], ParticipantIds = profiles.Take(2).ToList(), MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "pinned-overflow" }, token).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                    Require(run.State == "paused" && run.Outcome == "context_budget_exceeded" && runner.Compactions <= 3, "Pinned overflow must pause instead of silently truncating originals or repeatedly compacting them.");
                    foreach (InterjectionReceipt original in originals) Require((await database.ReadMessageAsync(context, pinned.Id, original.MessageId, token).ConfigureAwait(false)).Content.Length > 5000, "Pinned overflow altered authoritative context.");
                }
            }
            foreach (string failure in new[] { "missing_section", "duplicate_section", "invented_source", "missing_source", "expanded_memory" })
            {
                Conversation conversation = await database.CreateConversationAsync(context, "Reject " + failure, token).ConfigureAwait(false);
                for (int index = 0; index < 12; index++) await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "Original disputed evidence " + index + new string('x', 1500) }, token: token).ConfigureAwait(false);
                ContextFixtureRunner runner = new ContextFixtureRunner { SkipClarification = true, InvalidSummary = failure };
                await using (ConversationEngine engine = new ConversationEngine(database, runner, contextMaxCharacters: 12000, contextMaxMessages: 20))
                {
                    DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[2], ParticipantIds = profiles.Take(2).ToList(), MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "summary-validation" }, token).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                    List<ContextSummary> summaries = await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                    AttemptPage attempts = await database.Attempts.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                    Require(run.State == "paused" && attempts.Items.Any(attempt => attempt.ErrorCode == "summary_validation_failed"), "Invalid summary did not pause with a useful cause: " + failure);
                    Require(summaries.Count == 1 && summaries[0].Active && summaries[0].Sections.Count == 7, "Invalid replacement changed the last valid summary: " + failure);
                    Require((await database.ReadMessagesAsync(context, conversation.Id, token).ConfigureAwait(false)).Count(message => message.Content.StartsWith("Original disputed evidence", StringComparison.Ordinal)) == 12, "Invalid summary altered original evidence.");
                    if (failure != "missing_section") continue;
                    // A failed moderator rebuild must leave the last valid summary active; a successful one compacts originals, not the summary it replaces.
                    ContextSummary previous = summaries[0];
                    await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "rebuild_summary", IdempotencyKey = "rebuild-invalid" }, "fixture", token).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                    List<ContextSummary> afterFailure = await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token, activeOnly: true).ConfigureAwait(false);
                    Require(run.State == "paused" && run.Outcome == "summary_validation_failed" && run.RebuildFromOriginals, "A failed rebuild must pause with its cause and remain retryable: " + run.Outcome);
                    Require(afterFailure.Count == 1 && afterFailure[0].Id == previous.Id, "A failed rebuild deactivated the last valid summary.");
                    runner.InvalidSummary = "";
                    int before = runner.CompactionHistories.Count;
                    await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "rebuild_summary", IdempotencyKey = "rebuild-valid" }, "fixture", token).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                    Require(runner.CompactionHistories.Count > before && !runner.CompactionHistories[before].Contains(previous.MessageId), "A rebuild must compact original evidence rather than the summary it replaces.");
                    List<ContextSummary> rebuilt = await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                    Require(!run.RebuildFromOriginals && rebuilt.Count(summary => summary.Active) == 1 && rebuilt.Any(summary => summary.Id == previous.Id && !summary.Active), "A validated rebuild must replace the prior summary exactly once.");
                }
            }
            Conversation constrained = await database.CreateConversationAsync(context, "Pinned constraint attribution", token).ConfigureAwait(false);
            ContextFixtureRunner attributing = new ContextFixtureRunner { SkipClarification = true, InvalidSummary = "unattributed_constraint" };
            await using (ConversationEngine engine = new ConversationEngine(database, attributing, contextMaxCharacters: 12000, contextMaxMessages: 20))
            {
                InterjectionReceipt constraint = await engine.InterjectAsync(context, constrained.Id, new InterjectionRequest { Text = "Pinned: never exceed five percent of traffic.", Pinned = true, DeliveryMode = "next_turn", IdempotencyKey = "pinned-attribution" }, token).ConfigureAwait(false);
                for (int index = 0; index < 12; index++) await database.AppendMessageAsync(context, new Message { ConversationId = constrained.Id, Content = "Original constrained evidence " + index + new string('x', 1500) }, token: token).ConfigureAwait(false);
                DebateRun run = await engine.StartAsync(context, constrained.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[2], ParticipantIds = profiles.Take(2).ToList(), MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "pinned-attribution-run" }, token).ConfigureAwait(false);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                List<ContextSummary> summaries = await database.Memory.EnumerateAsync(context, constrained.Id, run.Id, token: token).ConfigureAwait(false);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, constrained.Id, run.Id, token: token).ConfigureAwait(false);
                Require(run.State == "paused" && run.Outcome == "summary_validation_failed" && summaries.Count == 0 && attempts.Items.Any(attempt => attempt.ErrorCode == "summary_validation_failed"), "A summary that does not attribute a supplied pinned constraint must be rejected: " + run.State + "/" + run.Outcome);
                List<GenerationAttempt> compactions = attempts.Items.Where(attempt => attempt.Purpose == "compaction").OrderBy(attempt => attempt.Number).ToList();
                Require(compactions.Count == 4 && compactions.Select(attempt => attempt.RepairNumber).SequenceEqual(new[] { 0, 1, 2, 3 }) && compactions.All(attempt => attempt.ErrorCode == "summary_validation_failed"), "An invalid summary must get correction requests up to the step retry limit (3) before pausing: " + compactions.Count);
                attributing.InvalidSummary = "";
                await engine.ApplyAsync(context, constrained.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "pinned-attribution-resume" }, "fixture", token).ConfigureAwait(false);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                summaries = await database.Memory.EnumerateAsync(context, constrained.Id, run.Id, token: token).ConfigureAwait(false);
                Require(run.Outcome == "consensus" && summaries.Any(summary => summary.Sections.Single(section => section.Kind == "constraints").Content.Contains(constraint.MessageId, StringComparison.Ordinal)), "A summary that attributes the pinned constraint must activate: " + run.Outcome);
                Require(attributing.Compactions >= 1, "The pinned-constraint fixture never compacted.");
            }
        }
        private static async Task VerifyHashesAsync(DatabaseDriverBase database, RequestContext context, string conversationId, ContextSummary summary, CancellationToken token)
        {
            Require(summary.Sources.Select(source => source.MessageId).SequenceEqual(summary.SourceIds), "Source hashes must match the same ordered manifest page.");
            foreach (SummarySource source in summary.Sources)
            {
                Message original = await database.ReadMessageAsync(context, conversationId, source.MessageId, token).ConfigureAwait(false);
                string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original.Content))).ToLowerInvariant();
                Require(digest == source.Digest, "A summary hash does not identify its original content.");
            }
        }
        internal static async Task InterruptCompactionAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-compaction-interrupt-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    List<string> profiles = new List<string>();
                    for (int index = 0; index < 3; index++) profiles.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Agent " + index, Executable = "fictional" }, token).ConfigureAwait(false)).Id);
                    foreach (string mode in new[] { "interrupt_now", "next_turn" })
                    {
                        Conversation conversation = await database.CreateConversationAsync(context, "Compaction " + mode, token).ConfigureAwait(false);
                        for (int index = 0; index < 10; index++) await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "Evidence " + index + new string('x', 1500) }, token: token).ConfigureAwait(false);
                        ContextFixtureRunner runner = new ContextFixtureRunner { HoldCompaction = true, PrivateId = "msg_unavailable" };
                        await using (ConversationEngine engine = new ConversationEngine(database, runner, contextMaxCharacters: 12000, contextMaxMessages: 20))
                        {
                            DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[2], ParticipantIds = profiles.Take(2).ToList(), MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "start" }, token).ConfigureAwait(false);
                            await runner.CompactionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                            await engine.InterjectAsync(context, conversation.Id, new InterjectionRequest { Text = "A new mandatory rollback threshold.", DeliveryMode = mode, Pinned = true, IdempotencyKey = "compaction-context" }, token).ConfigureAwait(false);
                            if (mode == "interrupt_now")
                            {
                                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                                Require(run.State == "paused" && run.Outcome == "interrupted", "Compaction interruption did not pause visibly.");
                                Require((await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false)).Count == 0, "Interrupted summary was activated.");
                                await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "resume" }, "fixture", token).ConfigureAwait(false);
                            }
                            else runner.ReleaseCompaction.TrySetResult(true);
                            run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                            Require(run.Outcome == "awaiting_clarification", "Fresh compaction could not continue after new context: " + run.Outcome);
                            List<ContextSummary> summaries = await database.Memory.EnumerateAsync(context, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                            Require(summaries.Count > 0 && summaries.All(summary => summary.ContextEpoch == 1), "An old-epoch summary became authoritative.");
                        }
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task<DebateRun> WaitAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, CancellationToken token)
        {
            for (int index = 0; index < 500; index++)
            {
                run = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                if (new[] { "paused", "completed", "failed", "cancelled" }.Contains(run.State)) return run;
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("Context fixture did not reach a boundary.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
