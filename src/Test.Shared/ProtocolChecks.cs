namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Run admission rejections, the single structured-output repair and moderator proposal edits.</summary>
    internal static class ProtocolChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-protocol-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    List<string> profiles = new List<string>();
                    for (int index = 0; index < 18; index++) profiles.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Agent " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);
                    string twin = (await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "agent 1", Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id;
                    string disabled = (await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Disabled agent", Executable = "fictional", WorkingDirectory = home, Enabled = false }, token).ConfigureAwait(false)).Id;
                    await AdmissionAsync(database, context, profiles, twin, disabled, token).ConfigureAwait(false);
                    await RepairAsync(database, context, profiles, token).ConfigureAwait(false);
                    await EditAsync(database, context, profiles, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        private static async Task AdmissionAsync(SqliteDatabaseDriver database, RequestContext context, List<string> profiles, string twin, string disabled, CancellationToken token)
        {
            database.Debates.MaximumParticipants = 16;
            Conversation conversation = await database.CreateConversationAsync(context, "Admission", token).ConfigureAwait(false);
            MultiAgentFixtureRunner runner = new MultiAgentFixtureRunner();
            await using (ConversationEngine engine = new ConversationEngine(database, runner))
            {
                List<Rejection> cases = new List<Rejection>
                {
                    new Rejection("one seat", Request(profiles, 1), "too_few_participants", "participantIds"),
                    new Rejection("over the service limit", Request(profiles, 17), "participant_limit", "participantIds"),
                    new Rejection("the same profile twice", With(Request(profiles, 2), request => request.ParticipantIds.Add(profiles[1])), "duplicate_participant", "participantIds"),
                    new Rejection("two seats with one name", With(Request(profiles, 2), request => request.ParticipantIds.Add(twin)), "duplicate_seat_name", "participantIds"),
                    new Rejection("an unavailable participant", With(Request(profiles, 2), request => request.ParticipantIds.Add("pro_missing")), "profile_not_found", "participantIds"),
                    new Rejection("a disabled participant", With(Request(profiles, 2), request => request.ParticipantIds.Add(disabled)), "disabled_profile", "participantIds"),
                    new Rejection("no orchestrator", With(Request(profiles, 2), request => request.OrchestratorId = ""), "missing_orchestrator", "orchestratorId"),
                    new Rejection("an unavailable orchestrator", With(Request(profiles, 2), request => request.OrchestratorId = "pro_missing"), "profile_not_found", "orchestratorId"),
                    new Rejection("a disabled orchestrator", With(Request(profiles, 2), request => request.OrchestratorId = disabled), "disabled_profile", "orchestratorId"),
                    new Rejection("zero rounds", With(Request(profiles, 2), request => request.MaxRounds = 0), "invalid_rounds", "maxRounds"),
                    new Rejection("a long countdown", With(Request(profiles, 2), request => request.InterventionSeconds = 601), "invalid_countdown", "interventionSeconds"),
                    new Rejection("a tiny context budget", With(Request(profiles, 2), request => request.Policy.MaxContextCharacters = 10), "invalid_policy", "policy.maxContextCharacters")
                };
                foreach (Rejection rejection in cases)
                {
                    DomainException? failure = null;
                    try { await engine.StartAsync(context, conversation.Id, rejection.Request, token).ConfigureAwait(false); }
                    catch (DomainException exception) { failure = exception; }
                    Require(failure != null && failure.Code == rejection.Code && failure.Field == rejection.Field, "Admission with " + rejection.Name + " must fail as " + rejection.Code + " on " + rejection.Field + ", not " + (failure == null ? "success" : failure.Code + " on " + failure.Field) + ".");
                    Require(failure!.Message.Length > 0 && !failure.Message.Contains("pro_", StringComparison.Ordinal), "Rejection messages must be readable and must not echo internal identifiers.");
                }
                Require(runner.Prompts.IsEmpty, "A rejected admission must not invoke any model.");
                List<DebateRun> runs = await database.Runs.EnumerateAsync(context, conversation.Id, token).ConfigureAwait(false);
                Require(runs.Count == 0, "A rejected admission must not create a run.");
                DebateRun followup = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Summarize the trade-offs.", ParticipantIds = new List<string> { profiles[1] }, IdempotencyKey = "single-followup" }, token).ConfigureAwait(false);
                followup = await WaitAsync(database, context, followup, token).ConfigureAwait(false);
                Require(followup.State == "completed" && followup.Outcome == "answered", "A single-seat follow-up must still run after rejected debates and complete as answered: " + followup.State + "/" + followup.Outcome);
            }
        }

        private static async Task RepairAsync(SqliteDatabaseDriver database, RequestContext context, List<string> profiles, CancellationToken token)
        {
            Conversation repaired = await database.CreateConversationAsync(context, "Repaired vote", token).ConfigureAwait(false);
            MultiAgentFixtureRunner once = new MultiAgentFixtureRunner();
            once.RawOnce["Agent 2:ratification"] = "I agree with the proposal.";
            await using (ConversationEngine engine = new ConversationEngine(database, once))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, repaired.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                Require(run.State == "completed" && run.Outcome == "consensus", "One invalid vote followed by a valid repair must still reach consensus: " + run.State + "/" + run.Outcome);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, repaired.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                string seat = run.Participants.Single(participant => participant.Name == "Agent 2").Id;
                List<GenerationAttempt> votes = attempts.Items.Where(item => item.ParticipantId == seat && item.Purpose == "ratification").OrderBy(item => item.Number).ToList();
                Require(votes.Count == 2 && votes[0].State == "failed" && votes[0].ErrorCode == "invalid_orchestration_output" && !votes[0].Repair && votes[1].State == "completed" && votes[1].Repair, "The invalid vote must be kept as failed and followed by exactly one repair attempt.");
                Require(attempts.Items.Count(item => item.Repair) == 1, "Only the invalid step is repaired.");
                Require(once.Prompts.Count(prompt => prompt.Contains("correction request 1 of 3", StringComparison.Ordinal)) == 1, "The repair prompt must ask for the required JSON and count the correction requests.");
                List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, repaired.Id, run.Id, token).ConfigureAwait(false);
                Require(proposals.Single().Agreements.Count == 4, "The invalid answer must not be recorded as a vote.");
            }
            Conversation persistent = await database.CreateConversationAsync(context, "Repair also fails", token).ConfigureAwait(false);
            MultiAgentFixtureRunner always = new MultiAgentFixtureRunner();
            always.RawRatification["Agent 3"] = "WE HAVE CONSENSUS\nAgent 0 said we agree, so I agree too.";
            await using (ConversationEngine engine = new ConversationEngine(database, always))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, persistent.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                Require(run.State == "paused" && run.Outcome == "invalid_orchestration_output", "A failed repair must pause for the moderator: " + run.State + "/" + run.Outcome);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, persistent.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                List<GenerationAttempt> failed = attempts.Items.Where(item => item.State == "failed").OrderBy(item => item.Number).ToList();
                Require(failed.Count == 4 && failed.Select(item => item.RepairNumber).SequenceEqual(new[] { 0, 1, 2, 3 }), "Repairs follow invalid output up to the step retry limit (3) before pausing: " + string.Join(",", failed.Select(item => item.RepairNumber)));
                Require(always.Prompts.Count(prompt => prompt.Contains("correction request 3 of 3; another invalid reply pauses", StringComparison.Ordinal)) == 1, "The last correction request must say that another invalid reply pauses.");
                always.RawRatification.Clear();
                await engine.ApplyAsync(context, persistent.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "resume-after-repair" }, "fixture", token).ConfigureAwait(false);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                attempts = await database.Attempts.EnumerateAsync(context, persistent.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                Require(run.Outcome == "consensus" && !attempts.Items.OrderBy(item => item.Number).Last(item => item.Purpose == "ratification").Repair, "A moderator retry after a failed repair is an ordinary attempt: " + run.Outcome);
            }
            Conversation garbled = await database.CreateConversationAsync(context, "Miscopied proposal ID", token).ConfigureAwait(false);
            MultiAgentFixtureRunner miscopy = new MultiAgentFixtureRunner();
            miscopy.RawOnce["Agent 2:ratification"] = "{\"proposalId\":\"prp_garbled_copy\",\"position\":\"agree\",\"rationale\":\"Agreed.\"}";
            await using (ConversationEngine engine = new ConversationEngine(database, miscopy))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, garbled.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, garbled.Id, run.Id, token).ConfigureAwait(false);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, garbled.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                Require(run.Outcome == "consensus" && attempts.Items.All(item => item.State == "completed") && proposals.Single().Agreements.Count == run.Participants.Count, "A vote whose proposal ID is miscopied (names no proposal of the run) must count for the open proposal: " + run.State + "/" + run.Outcome);
            }
            Conversation trailing = await database.CreateConversationAsync(context, "Text after the vote", token).ConfigureAwait(false);
            MultiAgentFixtureRunner remark = new MultiAgentFixtureRunner();
            remark.RawOnce["Agent 2:ratification"] = "{\"proposalId\":\"\",\"position\":\"agree\",\"rationale\":\"Agreed.\"}\n\nWait, one more thing.\n\n{\"proposalId\":\"x\",\"position\":\"agree\",\"rationale\":\"placeholder\"}";
            await using (ConversationEngine engine = new ConversationEngine(database, remark))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, trailing.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, trailing.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                Require(run.Outcome == "consensus" && attempts.Items.All(item => item.State == "completed"), "A vote followed by extra text must be read from its leading JSON object: " + run.State + "/" + run.Outcome);
            }
            Conversation stale = await database.CreateConversationAsync(context, "Vote on an earlier proposal", token).ConfigureAwait(false);
            MultiAgentFixtureRunner earlier = new MultiAgentFixtureRunner { Disagree = true };
            earlier.StaleOnce.Add("Agent 2:ratification");
            await using (ConversationEngine engine = new ConversationEngine(database, earlier))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, stale.Id, With(Request(profiles, 4), request => request.MaxRounds = 2), token).ConfigureAwait(false), token).ConfigureAwait(false);
                List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, stale.Id, run.Id, token).ConfigureAwait(false);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, stale.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                string newest = proposals.OrderByDescending(item => item.Id, StringComparer.Ordinal).First().Id;
                Require(run.Outcome == "max_rounds" && attempts.Items.Count(item => item.ErrorCode == "invalid_orchestration_output") == 1 && attempts.Items.Any(item => item.Purpose == "ratification" && item.RepairNumber == 1 && item.State == "completed"), "A vote naming an earlier proposal of the run must be repaired: " + run.State + "/" + run.Outcome);
                Require(earlier.Prompts.Any(prompt => prompt.StartsWith("Agent 2\n", StringComparison.Ordinal) && prompt.Contains("Set proposalId to exactly \"" + newest + "\"", StringComparison.Ordinal)), "The vote correction request must restate the exact proposal ID.");
                Require(earlier.Prompts.Any(prompt => prompt.StartsWith("Agent 2\n", StringComparison.Ordinal) && prompt.Contains("Proposal IDs in earlier messages belong to closed proposals. This vote's proposalId is \"" + newest + "\".", StringComparison.Ordinal)), "Every vote prompt must restate the open proposal ID after the frozen prompt.");
            }
            Conversation transient = await database.CreateConversationAsync(context, "Intermittent agent failure", token).ConfigureAwait(false);
            MultiAgentFixtureRunner flaky = new MultiAgentFixtureRunner();
            flaky.FailOnce["Agent 2:response"] = "provider_unavailable";
            await using (ConversationEngine engine = new ConversationEngine(database, flaky))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, transient.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, transient.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                string seat = run.Participants.Single(participant => participant.Name == "Agent 2").Id;
                List<GenerationAttempt> turns = attempts.Items.Where(item => item.ParticipantId == seat && item.Purpose == "response").OrderBy(item => item.Number).ToList();
                Require(run.Outcome == "consensus" && turns.Count == 2 && turns[0].ErrorCode == "provider_unavailable" && turns[1].State == "completed", "A transient failure must be retried automatically and the debate must continue: " + run.State + "/" + run.Outcome + "/" + turns.Count);
            }
            Conversation noRetry = await database.CreateConversationAsync(context, "Retries turned off", token).ConfigureAwait(false);
            MultiAgentFixtureRunner off = new MultiAgentFixtureRunner();
            off.FailOnce["Agent 2:response"] = "provider_unavailable";
            await using (ConversationEngine engine = new ConversationEngine(database, off))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, noRetry.Id, With(Request(profiles, 4), request => request.Policy.MaxStepRetries = 0), token).ConfigureAwait(false), token).ConfigureAwait(false);
                Require(run.State == "paused" && run.Outcome == "provider_unavailable", "With retries off, a transient failure must pause at once: " + run.State + "/" + run.Outcome);
            }
            Conversation lasting = await database.CreateConversationAsync(context, "Credential failure", token).ConfigureAwait(false);
            MultiAgentFixtureRunner refused = new MultiAgentFixtureRunner();
            refused.FailOnce["Agent 2:response"] = "provider_unauthorized";
            await using (ConversationEngine engine = new ConversationEngine(database, refused))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, lasting.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                Require(run.State == "paused" && run.Outcome == "provider_unauthorized", "A credential failure is not transient and must pause without a retry: " + run.State + "/" + run.Outcome);
            }
            Conversation proposal = await database.CreateConversationAsync(context, "Repaired proposal", token).ConfigureAwait(false);
            MultiAgentFixtureRunner synthesis = new MultiAgentFixtureRunner();
            synthesis.RawOnce["synthesis"] = "My proposal is to choose the reversible design.";
            await using (ConversationEngine engine = new ConversationEngine(database, synthesis))
            {
                DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, proposal.Id, Request(profiles, 2), token).ConfigureAwait(false), token).ConfigureAwait(false);
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, proposal.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                Require(run.Outcome == "consensus" && attempts.Items.Count(item => item.Purpose == "synthesis") == 2 && attempts.Items.Single(item => item.Repair).Purpose == "synthesis", "An invalid orchestrator proposal must be repaired once: " + run.Outcome);
            }
        }

        private static async Task EditAsync(SqliteDatabaseDriver database, RequestContext context, List<string> profiles, CancellationToken token)
        {
            Conversation edited = await database.CreateConversationAsync(context, "Edit races the final vote", token).ConfigureAwait(false);
            MultiAgentFixtureRunner racing = new MultiAgentFixtureRunner { HoldPhase = "ratification", HoldAfter = 3 };
            await using (ConversationEngine engine = new ConversationEngine(database, racing))
            {
                DebateRun run = await engine.StartAsync(context, edited.Id, Request(profiles, 4), token).ConfigureAwait(false);
                await racing.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                run = await database.Runs.ReadAsync(context, edited.Id, run.Id, token).ConfigureAwait(false);
                string original = run.ProposalId;
                await ExpectAsync(() => engine.ApplyAsync(context, edited.Id, run.Id, new CommandRequest { Action = "edit_proposal", Text = "Edited.", IdempotencyKey = "no-revision" }, "fixture", token), "revision_required", "An edit without the reviewed revision must be refused.").ConfigureAwait(false);
                await ExpectAsync(() => engine.ApplyAsync(context, edited.Id, run.Id, new CommandRequest { Action = "edit_proposal", Text = "Edited.", Revision = run.Revision - 1, IdempotencyKey = "stale-revision" }, "fixture", token), "revision_conflict", "An edit based on a stale view must be refused.").ConfigureAwait(false);
                await ExpectAsync(() => engine.ApplyAsync(context, edited.Id, run.Id, new CommandRequest { Action = "edit_proposal", Text = " ", Revision = run.Revision, IdempotencyKey = "blank" }, "fixture", token), "invalid_proposal", "A blank edit must be refused.").ConfigureAwait(false);
                CommandRequest edit = new CommandRequest { Action = "edit_proposal", Text = "Choose the reversible design, limited to five percent of traffic.", Revision = run.Revision, IdempotencyKey = "edit" };
                await engine.ApplyAsync(context, edited.Id, run.Id, edit, "fixture", token).ConfigureAwait(false);
                await engine.ApplyAsync(context, edited.Id, run.Id, edit, "fixture", token).ConfigureAwait(false);
                racing.Release.TrySetResult(true);
                run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, edited.Id, run.Id, token).ConfigureAwait(false);
                Require(run.Outcome == "consensus" && proposals.Count == 2, "An edit committed during the final vote must reopen voting on one replacement proposal: " + run.Outcome + "/" + proposals.Count);
                DebateProposal replaced = proposals.Single(item => item.Id == original);
                DebateProposal revision = proposals.Single(item => item.Id != original);
                Require(replaced.Superseded && !replaced.ModeratorEdited && replaced.Agreements.Count == 3, "The stale final vote must not be recorded, and earlier votes stay on the superseded proposal.");
                Require(!revision.Superseded && revision.ModeratorEdited && revision.SupersedesId == original && revision.Content == edit.Text, "The replacement must record the moderator text and what it replaced.");
                Require(revision.Agreements.Count(vote => vote.Position == "agree" && !vote.Superseded) == 4, "Every seat must vote again on the edited proposal.");
                AttemptPage attempts = await database.Attempts.EnumerateAsync(context, edited.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                Require(attempts.Items.All(item => item.State == "completed" && !item.Repair), "A vote on the replaced proposal is stale, not invalid output, and must not trigger a repair or pause.");
                Require(racing.Prompts.Count(prompt => prompt.Contains("limited to five percent", StringComparison.Ordinal) && prompt.Contains("Independently evaluate", StringComparison.Ordinal)) >= 4, "Each seat must evaluate the edited text.");
                await ExpectAsync(() => engine.ApplyAsync(context, edited.Id, run.Id, new CommandRequest { Action = "edit_proposal", Text = "Too late.", Revision = run.Revision, IdempotencyKey = "after-finalization" }, "fixture", token), "no_open_proposal", "An edit after finalization must be refused because the earlier transaction won.").ConfigureAwait(false);
            }
        }

        private static async Task ExpectAsync(Func<Task> action, string code, string message)
        {
            DomainException? failure = null;
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) { failure = exception; }
            Require(failure != null && failure.Code == code, message + " Got " + (failure == null ? "success" : failure.Code) + ".");
        }
        private static CreateRunRequest Request(List<string> profiles, int count)
        { return new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[0], ParticipantIds = profiles.Take(count).ToList(), Question = "Which design?", MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = Guid.NewGuid().ToString("N") }; }
        private static CreateRunRequest With(CreateRunRequest request, Action<CreateRunRequest> change) { change(request); return request; }
        private static async Task<DebateRun> WaitAsync(SqliteDatabaseDriver database, RequestContext context, DebateRun run, CancellationToken token)
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
