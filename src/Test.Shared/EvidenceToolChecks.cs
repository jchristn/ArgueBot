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
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Model-facing evidence tool contracts: paging, filters, literal search, linked evidence, gaps and debate state.</summary>
    internal static class EvidenceToolChecks
    {
        internal static async Task SqliteAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-tools-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await RunAsync(database, home, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        internal static async Task RunAsync(DatabaseDriverBase database, string workingDirectory, CancellationToken token)
        {
            RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            List<string> profiles = new List<string>();
            for (int index = 0; index < 4; index++) profiles.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Tool agent " + index + " " + suffix, Executable = "fictional", WorkingDirectory = workingDirectory }, token).ConfigureAwait(false)).Id);
            Conversation conversation = await database.CreateConversationAsync(context, "Evidence tools " + suffix, token).ConfigureAwait(false);
            MultiAgentFixtureRunner runner = new MultiAgentFixtureRunner();
            runner.Positions["Tool agent 1 " + suffix] = "disagree";
            await using (ConversationEngine engine = new ConversationEngine(database, runner))
            {
                InterjectionReceipt pinned = await engine.InterjectAsync(context, conversation.Id, new InterjectionRequest { Text = "Constraint: keep every rollout reversible.", Pinned = true, DeliveryMode = "next_turn", IdempotencyKey = "pinned-" + suffix }, token).ConfigureAwait(false);
                Message literal = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = new string('a', 2000) + " Ship 100% CANARY_rollout after review. " + new string('z', 2000) }, token: token).ConfigureAwait(false);
                await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "Decoy: ship 1000 canaryXrollout without the literal percent." }, token: token).ConfigureAwait(false);
                Message accented = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "ÉCLAIR évidence from the pilot." }, token: token).ConfigureAwait(false);
                CreateRunRequest request = new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[0], ParticipantIds = profiles.Take(3).ToList(), Question = "Which rollout?", MaxRounds = 2, InterventionSeconds = 0, IdempotencyKey = "tools-" + suffix };
                DebateRun run = await engine.StartAsync(context, conversation.Id, request, token).ConfigureAwait(false);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                Require(run.State == "completed" && run.Outcome == "max_rounds", "The evidence fixture debate did not finish both rounds: " + run.State + "/" + run.Outcome);
                Message hidden = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, RunId = run.Id, Kind = "steer", RecipientId = run.Participants[1].Id, Content = "HIDDEN_TOOL_EVIDENCE for another seat." }, token: token).ConfigureAwait(false);
                string seat = run.Participants[0].Id;
                ConversationTools tools = new ConversationTools(database, context, run, seat, long.MaxValue, "tools-" + suffix);

                List<JsonElement> pages = new List<JsonElement>(); long after = 0; int guard = 0;
                do
                {
                    JsonElement page = await CallAsync(tools, "list_turns", new { after, limit = 3 }, token).ConfigureAwait(false);
                    pages.AddRange(page.GetProperty("items").EnumerateArray());
                    after = page.GetProperty("nextAfter").ValueKind == JsonValueKind.Null ? -1 : page.GetProperty("nextAfter").GetInt64();
                    Require(page.GetProperty("items").GetArrayLength() <= 3, "list_turns exceeded its page size.");
                } while (after >= 0 && ++guard < 100);
                List<long> sequences = pages.Select(item => item.GetProperty("sequence").GetInt64()).ToList();
                Require(sequences.SequenceEqual(sequences.OrderBy(value => value).Distinct()), "Following nextAfter must page every visible turn exactly once in order.");
                Require(pages.All(item => item.GetProperty("id").GetString() != hidden.Id), "list_turns exposed another seat's private context.");
                JsonElement ownTurns = await CallAsync(tools, "list_turns", new { seatId = seat, limit = 20 }, token).ConfigureAwait(false);
                Require(ownTurns.GetProperty("items").GetArrayLength() == 4 && ownTurns.GetProperty("items").EnumerateArray().All(item => item.GetProperty("seatId").GetString() == seat), "The seat filter must return exactly that seat's two responses and two votes.");
                JsonElement votes = await CallAsync(tools, "list_turns", new { phase = "ratification", limit = 20 }, token).ConfigureAwait(false);
                Require(votes.GetProperty("items").GetArrayLength() == 6 && votes.GetProperty("items").EnumerateArray().All(item => item.GetProperty("phase").GetString() == "ratification"), "The phase filter must return each seat's vote in both rounds.");
                JsonElement moderator = await CallAsync(tools, "list_turns", new { phase = "moderator", limit = 20 }, token).ConfigureAwait(false);
                Require(moderator.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetString() == pinned.MessageId), "The moderator phase filter must include moderator context.");
                string invalidSeat = await tools.ExecuteAsync(new ConversationToolCall { Name = "list_turns", CallId = "bad-seat", ArgumentsJson = "{\"seatId\":\"par_unknown\"}" }, token).ConfigureAwait(false);
                Require(invalidSeat.Contains("invalid_seat", StringComparison.Ordinal), "An unknown seat filter must be rejected.");

                JsonElement search = await CallAsync(tools, "search_context", new { query = "100% canary_rollout" }, token).ConfigureAwait(false);
                List<JsonElement> matches = search.GetProperty("items").EnumerateArray().ToList();
                Require(matches.Count == 1 && matches[0].GetProperty("id").GetString() == literal.Id, "Search must treat % and _ literally and ignore case.");
                string snippet = matches[0].GetProperty("snippet").GetString()!;
                Require(snippet.Contains("100% CANARY_rollout", StringComparison.Ordinal) && snippet.StartsWith("...", StringComparison.Ordinal) && snippet.Length < 800 && matches[0].GetProperty("matchOffset").GetInt32() == 2006 && matches[0].GetProperty("truncated").GetBoolean(), "Search snippets must be centered on the match, not the start of the turn.");
                JsonElement unicode = await CallAsync(tools, "search_context", new { query = "éclair" }, token).ConfigureAwait(false);
                Require(unicode.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetString() == accented.Id), "Search must fold non-ASCII case the same way on every database.");
                JsonElement secret = await CallAsync(tools, "search_context", new { query = "HIDDEN_TOOL_EVIDENCE" }, token).ConfigureAwait(false);
                Require(secret.GetProperty("items").GetArrayLength() == 0, "Search exposed another seat's private context.");
                string blank = await tools.ExecuteAsync(new ConversationToolCall { Name = "search_context", CallId = "blank", ArgumentsJson = "{\"query\":\" \"}" }, token).ConfigureAwait(false);
                Require(blank.Contains("missing_query", StringComparison.Ordinal), "An empty search must be refused instead of listing everything.");

                JsonElement details = await CallAsync(tools, "get_supporting_details", new { ids = new[] { literal.Id, hidden.Id, "msg_missing" }, length = 100 }, token).ConfigureAwait(false);
                Require(details.GetProperty("evidence").GetArrayLength() == 1 && details.GetProperty("evidence")[0].GetProperty("id").GetString() == literal.Id && details.GetProperty("evidence")[0].GetProperty("attribution").GetString()!.Length > 0 && details.GetProperty("evidence")[0].GetProperty("nextOffset").GetInt32() == 100, "Supporting details must return bounded visible evidence with attribution.");
                List<string> gaps = details.GetProperty("gaps").EnumerateArray().Select(gap => gap.GetProperty("id").GetString()!).ToList();
                Require(gaps.Count == 2 && gaps.Contains(hidden.Id) && gaps.Contains("msg_missing") && !details.ToString().Contains("HIDDEN_TOOL_EVIDENCE", StringComparison.Ordinal), "Supporting details must report private and missing IDs as gaps without their content.");
                JsonElement noMatch = await CallAsync(tools, "get_supporting_details", new { query = "no such evidence anywhere" }, token).ConfigureAwait(false);
                Require(noMatch.GetProperty("items").GetArrayLength() == 0 && noMatch.GetProperty("gaps").GetArrayLength() == 1, "A search without evidence must report a gap instead of inventing support.");

                JsonElement state = await CallAsync(tools, "get_debate_state", new { }, token).ConfigureAwait(false);
                Require(state.GetProperty("constraints").EnumerateArray().Any(item => item.GetProperty("id").GetString() == pinned.MessageId), "Debate state must list pinned moderator constraints.");
                List<JsonElement> earlier = state.GetProperty("earlierPositions").EnumerateArray().ToList();
                Require(earlier.Count == 3 && earlier.All(item => item.GetProperty("proposalId").GetString() != run.ProposalId) && earlier.Count(item => item.GetProperty("position").GetString() == "disagree") == 1, "Debate state must report recorded positions on earlier proposals and omit votes on the open proposal.");
                Require(state.GetProperty("seats").GetArrayLength() == 3 && state.GetProperty("round").GetInt32() == 2, "Debate state must include the roster and round.");
            }
        }

        private static async Task<JsonElement> CallAsync(ConversationTools tools, string name, object arguments, CancellationToken token)
        {
            string json = await tools.ExecuteAsync(new ConversationToolCall { Name = name, CallId = name + ":" + Guid.NewGuid().ToString("N"), ArgumentsJson = JsonSerializer.Serialize(arguments, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }, token).ConfigureAwait(false);
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                Require(document.RootElement.GetProperty("success").GetBoolean(), name + " failed: " + json);
                return document.RootElement.GetProperty("result").Clone();
            }
        }
        private static async Task<DebateRun> WaitAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, CancellationToken token)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                do
                {
                    await Task.Delay(10, deadline.Token).ConfigureAwait(false);
                    run = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, deadline.Token).ConfigureAwait(false);
                } while (run.State != "completed" && run.State != "paused" && run.State != "failed");
                return run;
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
