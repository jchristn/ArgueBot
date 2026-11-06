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

    internal static class MultiAgentChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-panel-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    List<string> profiles = new List<string>();
                    for (int index = 0; index < 64; index++) profiles.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Agent " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);
                    foreach (int count in new[] { 2, 3, 4, 8, 16, 32, 64 })
                    {
                        database.Debates.MaximumParticipants = Math.Max(16, count);
                        Conversation conversation = await database.CreateConversationAsync(context, "Panel " + count, token).ConfigureAwait(false);
                        await using (ConversationEngine engine = new ConversationEngine(database, new MultiAgentFixtureRunner()))
                        {
                            DebateRun run = await engine.StartAsync(context, conversation.Id, Request(profiles, count), token).ConfigureAwait(false);
                            run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                            List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                            Require(run.Outcome == "consensus" && proposals.Count == 1 && proposals[0].Agreements.Count == count, "Consensus requires every seat on the same proposal: " + run.Outcome);
                            AttemptPage attempts = await database.Attempts.EnumerateAsync(context, conversation.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                            Require(attempts.TotalCount == 2 * count + 2 && attempts.Items.All(item => item.State == "completed"), "Every response and explicit vote is durable.");
                            Require(attempts.Items.Count(item => item.Purpose == "ratification") == count, "Attempt purpose survives persistence.");
                        }
                    }
                    foreach (string phase in new[] { "question", "response", "synthesis", "ratification" })
                    {
                        Conversation interrupted = await database.CreateConversationAsync(context, "Interrupt " + phase, token).ConfigureAwait(false);
                        MultiAgentFixtureRunner held = new MultiAgentFixtureRunner { HoldPhase = phase, HoldAfter = phase == "ratification" ? 3 : 0 };
                        await using (ConversationEngine engine = new ConversationEngine(database, held))
                        {
                            DebateRun run = await engine.StartAsync(context, interrupted.Id, Request(profiles, 4), token).ConfigureAwait(false);
                            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                            InterjectionReceipt receipt = await engine.InterjectAsync(context, interrupted.Id, new InterjectionRequest { Text = "A new mandatory constraint.", DeliveryMode = "interrupt_now", IdempotencyKey = "interrupt" }, token).ConfigureAwait(false);
                            Require(receipt.RunId == run.Id && receipt.ContextEpoch == 1, "Implicit active-run targeting lost context epoch.");
                            run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                            Require(run.State == "paused" && run.Outcome == "interrupted", "Interrupt did not cancel and pause " + phase + ": " + run.Outcome);
                            AttemptPage interruptedAttempts = await database.Attempts.EnumerateAsync(context, interrupted.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                            Require(interruptedAttempts.Items.Any(attempt => attempt.Purpose == phase && attempt.State == "interrupted"), "Partial phase was not retained as interrupted.");
                            await engine.ApplyAsync(context, interrupted.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "resume" }, "fixture", token).ConfigureAwait(false);
                            run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                            List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, interrupted.Id, run.Id, token).ConfigureAwait(false);
                            if (phase == "ratification") Require(proposals.Any(proposal => proposal.ContextEpoch == 0 && proposal.Agreements.Count == 3), "Earlier votes must remain historical rather than carrying into the new epoch.");
                            Require(run.Outcome == "consensus" && proposals.Any(proposal => proposal.ContextEpoch == 1 && proposal.Agreements.Count == 4), "Resume counted stale agreement after " + phase);
                        }
                    }
                    Conversation dissent = await database.CreateConversationAsync(context, "Dissent", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner dissenting = new MultiAgentFixtureRunner { Disagree = true };
                    await using (ConversationEngine engine = new ConversationEngine(database, dissenting))
                    {
                        DebateRun run = await engine.StartAsync(context, dissent.Id, Request(profiles, 4), token).ConfigureAwait(false);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        Require(run.Outcome == "max_rounds", "Dissent cannot become unanimous consensus.");
                        dissenting.Disagree = false;
                        await engine.ApplyAsync(context, dissent.Id, run.Id, new CommandRequest { Action = "extend", Rounds = 1, IdempotencyKey = "extend" }, "fixture", token).ConfigureAwait(false);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        List<DebateProposal> extended = await database.Debates.ReadProposalsAsync(context, dissent.Id, run.Id, token).ConfigureAwait(false);
                        Require(run.State == "completed" && run.Outcome == "consensus" && run.Round == 2 && extended.Count == 2, "Extending a debate that reached its round limit must open a new question round: " + run.State + "/" + run.Outcome + "/" + run.Round + "/" + extended.Count);
                        Require(extended.Count(proposal => proposal.Agreements.Any(agreement => agreement.Position == "disagree")) == 1, "The earlier dissent must remain recorded after an extension.");
                        MessagePage tailPage = await database.ReadMessagePageAsync(context, dissent.Id, 1, 3, tail: true, token: token).ConfigureAwait(false);
                        List<int?> questionRounds = (await database.ReadMessagePageAsync(context, dissent.Id, 1, 100, token: token).ConfigureAwait(false)).Items.Where(item => item.Kind == "question").Select(item => item.Round).ToList();
                        Require(questionRounds.SequenceEqual(new int?[] { 1, 2 }) && tailPage.PageNumber > 1 && tailPage.Items.Where(item => item.Kind == "ratification").All(item => item.Round == 2), "Message pages must carry each generated message's debate round, including on later pages: " + string.Join(",", questionRounds));
                    }
                    Conversation validation = await database.CreateConversationAsync(context, "Delivery validation", token).ConfigureAwait(false);
                    await using (ConversationEngine engine = new ConversationEngine(database, new MultiAgentFixtureRunner { HoldResponse = true }))
                    {
                        DebateRun run = await engine.StartAsync(context, validation.Id, Request(profiles, 2), token).ConfigureAwait(false);
                        foreach (string mode in new[] { "", "later", "NEXT_TURN" })
                        {
                            bool rejected = false;
                            try { await engine.InterjectAsync(context, validation.Id, new InterjectionRequest { Text = "Context.", DeliveryMode = mode, IdempotencyKey = "mode-" + mode }, token).ConfigureAwait(false); }
                            catch (DomainException exception) when (exception.Code == "invalid_delivery_mode") { rejected = true; }
                            Require(rejected, "A missing or unknown delivery mode must be rejected: '" + mode + "'.");
                        }
                        InterjectionReceipt implicitTarget = await engine.InterjectAsync(context, validation.Id, new InterjectionRequest { Text = "Implicit target.", DeliveryMode = "next_turn", IdempotencyKey = "implicit" }, token).ConfigureAwait(false);
                        List<InterjectionReceipt> stored = await database.Interjections.EnumerateAsync(context, validation.Id, token: token).ConfigureAwait(false);
                        Require(stored.Count == 1 && stored[0].RunId == run.Id && implicitTarget.RunId == run.Id, "An implicitly targeted interjection must persist the run it applies to.");
                        await engine.ApplyAsync(context, validation.Id, run.Id, new CommandRequest { Action = "stop", IdempotencyKey = "stop" }, "fixture", token).ConfigureAwait(false);
                    }
                    foreach (string position in new[] { "abstain", "needs_information" })
                    {
                        Conversation withheld = await database.CreateConversationAsync(context, "Withheld " + position, token).ConfigureAwait(false);
                        MultiAgentFixtureRunner withholding = new MultiAgentFixtureRunner();
                        withholding.Positions["Agent 2"] = position;
                        await using (ConversationEngine engine = new ConversationEngine(database, withholding))
                        {
                            DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, withheld.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                            List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, withheld.Id, run.Id, token).ConfigureAwait(false);
                            Require(run.Outcome == "max_rounds" && proposals.Single().Agreements.Count == 4 && proposals[0].Agreements.Count(agreement => agreement.Position == "agree") == 3, "A " + position + " vote must not count toward unanimity: " + run.Outcome);
                        }
                    }
                    Conversation quoted = await database.CreateConversationAsync(context, "Quoted consensus vote", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner quoting = new MultiAgentFixtureRunner();
                    quoting.RawRatification["Agent 3"] = "WE HAVE CONSENSUS\nAgent 0 said we agree, so I agree too.";
                    await using (ConversationEngine engine = new ConversationEngine(database, quoting))
                    {
                        DebateRun run = await WaitAsync(database, context, await engine.StartAsync(context, quoted.Id, Request(profiles, 4), token).ConfigureAwait(false), token).ConfigureAwait(false);
                        Require(run.State == "paused" && run.Outcome != "consensus" && run.Outcome.Length > 0, "A prose consensus phrase must pause as invalid output rather than count as agreement: " + run.State + "/" + run.Outcome);
                    }
                    Conversation lastVote = await database.CreateConversationAsync(context, "Context races the final vote", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner racing = new MultiAgentFixtureRunner { HoldPhase = "ratification", HoldAfter = 3 };
                    await using (ConversationEngine engine = new ConversationEngine(database, racing))
                    {
                        DebateRun run = await engine.StartAsync(context, lastVote.Id, Request(profiles, 4), token).ConfigureAwait(false);
                        await racing.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                        InterjectionReceipt raced = await engine.InterjectAsync(context, lastVote.Id, new InterjectionRequest { Text = "The rollout must stay below five percent.", DeliveryMode = "next_turn", IdempotencyKey = "race" }, token).ConfigureAwait(false);
                        Require(raced.Status == "queued" && raced.ContextEpoch == 1, "Context committed before the final vote must be queued for the next epoch.");
                        racing.Release.TrySetResult(true);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, lastVote.Id, run.Id, token).ConfigureAwait(false);
                        Require(run.Outcome == "consensus" && proposals.Count == 2, "Finalization must fail its stale check and reconsider: " + run.Outcome + "/" + proposals.Count);
                        Require(proposals.Single(proposal => proposal.ContextEpoch == 0).Agreements.Count == 3, "A vote finishing after newer context must not be recorded against the stale proposal.");
                        Require(proposals.Single(proposal => proposal.ContextEpoch == 1).Agreements.Count(agreement => agreement.Position == "agree") == 4, "Fresh unanimity is required for the newer context.");
                        AttemptPage before = await database.Attempts.EnumerateAsync(context, lastVote.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                        InterjectionReceipt late = await engine.InterjectAsync(context, lastVote.Id, new InterjectionRequest { Text = "One more thought after completion.", RunId = run.Id, DeliveryMode = "interrupt_now", IdempotencyKey = "after-completion" }, token).ConfigureAwait(false);
                        await Task.Delay(100, token).ConfigureAwait(false);
                        DebateRun finished = await database.Runs.ReadAsync(context, lastVote.Id, run.Id, token).ConfigureAwait(false);
                        AttemptPage after = await database.Attempts.EnumerateAsync(context, lastVote.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                        Require(late.Status == "post_run" && finished.State == "completed" && finished.Outcome == "consensus" && after.TotalCount == before.TotalCount, "Context after finalization must be retained without reopening the run or invoking a model.");
                    }
                    Conversation directed = await database.CreateConversationAsync(context, "Private directive before finalization", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner directing = new MultiAgentFixtureRunner { HoldPhase = "ratification", HoldAfter = 3 };
                    await using (ConversationEngine engine = new ConversationEngine(database, directing))
                    {
                        DebateRun run = await engine.StartAsync(context, directed.Id, Request(profiles, 4), token).ConfigureAwait(false);
                        await directing.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(context, directed.Id, run.Id, token).ConfigureAwait(false);
                        string recipient = run.Participants.Single(seat => seat.Name == "Agent 1").Id;
                        InterjectionReceipt hidden = await engine.InterjectAsync(context, directed.Id, new InterjectionRequest { Text = "PRIVATE_DIRECTIVE_ONLY: weigh the rollback cost.", RecipientId = recipient, DeliveryMode = "interrupt_now", IdempotencyKey = "private" }, token).ConfigureAwait(false);
                        Require(hidden.ContextEpoch == 0 && hidden.Status == "queued", "A private directive must not advance the shared epoch or interrupt another seat's turn: " + hidden.ContextEpoch + "/" + hidden.Status);
                        directing.Release.TrySetResult(true);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, directed.Id, run.Id, token).ConfigureAwait(false);
                        Require(run.Outcome == "consensus" && run.ContextEpoch == 0 && proposals.Count == 1, "A private directive must not reset shared round progress: " + run.Outcome + "/" + run.ContextEpoch + "/" + proposals.Count);
                        List<DebateAgreement> recipientVotes = proposals[0].Agreements.Where(vote => vote.ParticipantId == recipient).ToList();
                        Require(recipientVotes.Count == 2 && recipientVotes.Count(vote => vote.Superseded) == 1 && proposals[0].Agreements.Count(vote => !vote.Superseded && vote.Position == "agree") == 4, "The recipient must vote again with the directive before finalization, with its earlier vote kept as superseded.");
                        Require(directing.Prompts.Where(prompt => !prompt.StartsWith("Agent 1\n", StringComparison.Ordinal)).All(prompt => !prompt.Contains("PRIVATE_DIRECTIVE_ONLY", StringComparison.Ordinal)), "A private directive reached another seat or the orchestrator.");
                        Require(directing.Prompts.All(prompt => !prompt.Contains("epoch: 1", StringComparison.Ordinal)), "A private directive changed the context epoch shown to the panel.");
                        Require(directing.Prompts.Any(prompt => prompt.StartsWith("Agent 1\n", StringComparison.Ordinal) && prompt.Contains("PRIVATE_DIRECTIVE_ONLY", StringComparison.Ordinal)), "The recipient never received its private directive.");
                        List<InterjectionReceipt> deliveries = await database.Interjections.EnumerateAsync(context, directed.Id, token: token).ConfigureAwait(false);
                        Require(deliveries.Single().DeliveredParticipantIds.SequenceEqual(new[] { recipient }), "Delivery records must show only the recipient.");
                    }
                    Conversation replaced = await database.CreateConversationAsync(context, "Replace a seat", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner replacing = new MultiAgentFixtureRunner { HoldResponse = true };
                    await using (ConversationEngine engine = new ConversationEngine(database, replacing))
                    {
                        DebateRun run = await engine.StartAsync(context, replaced.Id, Request(profiles, 3), token).ConfigureAwait(false);
                        await replacing.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(context, replaced.Id, run.Id, token).ConfigureAwait(false);
                        string oldSeat = run.Participants.Single(seat => seat.Name == "Agent 1").Id;
                        await engine.ChangeRosterAsync(context, replaced.Id, run.Id, new RosterChangeRequest { Action = "replace", ParticipantId = oldSeat, ProfileId = profiles[5], Revision = run.Revision, IdempotencyKey = "replace" }, token).ConfigureAwait(false);
                        replacing.Release.TrySetResult(true);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, replaced.Id, run.Id, token).ConfigureAwait(false);
                        Require(run.Outcome == "consensus" && run.Participants.Count == 3 && run.Participants.Any(seat => seat.Name == "Agent 5") && run.Participants.All(seat => seat.Id != oldSeat), "Replacement must create a new seat and close the old one: " + run.Outcome);
                        Require(proposals.Last().Agreements.Count == 3 && proposals.Last().Agreements.All(agreement => agreement.ParticipantId != oldSeat), "A replacement's agreement must not be counted as its predecessor's.");
                    }
                    Conversation removed = await database.CreateConversationAsync(context, "Remove a dissenting seat", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner removing = new MultiAgentFixtureRunner { Disagree = true, HoldPhase = "question", HoldAfter = 1 };
                    await using (ConversationEngine engine = new ConversationEngine(database, removing))
                    {
                        CreateRunRequest twoRounds = Request(profiles, 4); twoRounds.MaxRounds = 2;
                        DebateRun run = await engine.StartAsync(context, removed.Id, twoRounds, token).ConfigureAwait(false);
                        await removing.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(context, removed.Id, run.Id, token).ConfigureAwait(false);
                        string dissenter = run.Participants.Single(seat => seat.Name == "Agent 1").Id;
                        await engine.ChangeRosterAsync(context, removed.Id, run.Id, new RosterChangeRequest { Action = "remove", ParticipantId = dissenter, Revision = run.Revision, IdempotencyKey = "remove" }, token).ConfigureAwait(false);
                        removing.Release.TrySetResult(true);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, removed.Id, run.Id, token).ConfigureAwait(false);
                        Require(run.Outcome == "consensus" && run.Participants.Count == 3 && run.RosterRevision == 2, "Removal must apply at a boundary and require fresh agreement from the remaining seats: " + run.Outcome);
                        Require(proposals.Any(proposal => proposal.Agreements.Any(agreement => agreement.ParticipantId == dissenter && agreement.Position == "disagree")), "The removed seat's dissent must remain in the record.");
                    }
                    Conversation minimum = await database.CreateConversationAsync(context, "Two-seat minimum", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner holding = new MultiAgentFixtureRunner { HoldResponse = true };
                    await using (ConversationEngine engine = new ConversationEngine(database, holding))
                    {
                        DebateRun run = await engine.StartAsync(context, minimum.Id, Request(profiles, 2), token).ConfigureAwait(false);
                        await holding.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(context, minimum.Id, run.Id, token).ConfigureAwait(false);
                        bool rejected = false;
                        try { await engine.ChangeRosterAsync(context, minimum.Id, run.Id, new RosterChangeRequest { Action = "remove", ParticipantId = run.Participants[1].Id, Revision = run.Revision, IdempotencyKey = "below-minimum" }, token).ConfigureAwait(false); }
                        catch (DomainException exception) when (exception.Code == "minimum_roster") { rejected = true; }
                        Require(rejected, "Removing a seat from a two-seat debate must be refused with replacement guidance.");
                        await engine.ApplyAsync(context, minimum.Id, run.Id, new CommandRequest { Action = "stop", IdempotencyKey = "stop-minimum" }, "fixture", token).ConfigureAwait(false);
                    }
                    Conversation single = await database.CreateConversationAsync(context, "Single seat", token).ConfigureAwait(false);
                    await using (ConversationEngine engine = new ConversationEngine(database, new MultiAgentFixtureRunner()))
                    {
                        bool rejected = false;
                        try { await engine.StartAsync(context, single.Id, Request(profiles, 1), token).ConfigureAwait(false); }
                        catch (DomainException) { rejected = true; }
                        Require(rejected, "A one-seat debate must be rejected before any model call.");
                    }
                    Conversation changed = await database.CreateConversationAsync(context, "New context and roster", token).ConfigureAwait(false);
                    MultiAgentFixtureRunner runner = new MultiAgentFixtureRunner { HoldResponse = true };
                    await using (ConversationEngine engine = new ConversationEngine(database, runner))
                    {
                        DebateRun run = await engine.StartAsync(context, changed.Id, Request(profiles, 4), token).ConfigureAwait(false);
                        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                        InterjectionRequest request = new InterjectionRequest { Text = "A reversible rollout is required.", RunId = run.Id, DeliveryMode = "next_turn", IdempotencyKey = "context" };
                        InterjectionReceipt receipt = await engine.InterjectAsync(context, changed.Id, request, token).ConfigureAwait(false);
                        InterjectionReceipt retry = await engine.InterjectAsync(context, changed.Id, request, token).ConfigureAwait(false);
                        Require(receipt.Id == retry.Id && receipt.ContextEpoch == 1, "Context retry must not increment its epoch.");
                        request.DeliveryMode = "interrupt_now";
                        try { await engine.InterjectAsync(context, changed.Id, request, token).ConfigureAwait(false); throw new InvalidDataException("Changed delivery mode reused a command key."); }
                        catch (DomainException exception) when (exception.Code == "idempotency_conflict") { }
                        run = await database.Runs.ReadAsync(context, changed.Id, run.Id, token).ConfigureAwait(false);
                        await engine.ChangeRosterAsync(context, changed.Id, run.Id, new RosterChangeRequest { Action = "add", ProfileId = profiles[4], Revision = run.Revision, IdempotencyKey = "add" }, token).ConfigureAwait(false);
                        runner.Release.TrySetResult(true);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        Require(run.Participants.Count == 5 && run.RosterRevision == 2 && run.Outcome == "consensus", "Boundary roster changes must require fresh agreement: " + run.Outcome);
                        List<InterjectionReceipt> deliveries = await database.Interjections.EnumerateAsync(context, changed.Id, token: token).ConfigureAwait(false);
                        Require(deliveries.Single().DeliveredParticipantIds.Count == 5, "Each seat must receive the new context.");
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static CreateRunRequest Request(List<string> profiles, int count)
        { return new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = profiles[0], ParticipantIds = profiles.Take(count).ToList(), Question = "Which design?", MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = Guid.NewGuid().ToString("N") }; }
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
