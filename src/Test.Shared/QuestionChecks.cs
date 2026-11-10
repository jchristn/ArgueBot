namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;

    /// <summary>Questions agents ask the user: storage, the held and parked lifecycle, budgets and every transport.</summary>
    internal static class QuestionChecks
    {
        private static RequestContext Context() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, Role = "admin" }; }

        private static async Task UsingHomeAsync(string label, Func<SqliteDatabaseDriver, RequestContext, string, CancellationToken, Task> body, CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-questions-" + label + "-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await body(database, Context(), home, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        private static async Task<List<string>> SeatsAsync(DatabaseDriverBase database, RequestContext context, string home, CancellationToken token)
        {
            List<string> seats = new List<string>();
            for (int index = 0; index < 3; index++) seats.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Question agent " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);
            return seats;
        }

        private static async Task<DebateRun> StartAsync(ConversationEngine engine, DatabaseDriverBase database, RequestContext context, List<string> seats, DebatePolicy policy, string key, CancellationToken token)
        {
            Conversation conversation = await database.CreateConversationAsync(context, "Questions " + key, token).ConfigureAwait(false);
            policy.Intake = "off";
            return await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "How should we roll out?", MaxRounds = 1, InterventionSeconds = 0, Policy = policy, IdempotencyKey = "run-" + key }, token).ConfigureAwait(false);
        }

        private static async Task<DebateRun> WaitAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, Func<DebateRun, bool> done, CancellationToken token, int seconds = 20)
        {
            DateTime limit = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < limit)
            {
                run = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                if (done(run)) return run;
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("The run did not reach the expected state; it is " + run.State + "/" + run.Outcome + ".");
        }

        private static bool Finished(DebateRun run) { return run.State == "completed" || run.State == "failed" || run.State == "cancelled" || run.State == "paused"; }

        private static async Task<QuestionRequest> OpenAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, string holdState, CancellationToken token)
        {
            DateTime limit = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < limit)
            {
                QuestionRequest? open = (await database.Questions.RunRequestsAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false)).FirstOrDefault(item => item.Status == "open" && item.HoldState == holdState);
                if (open != null) return open;
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            throw new InvalidDataException("No open question reached hold state " + holdState + ".");
        }

        private static QuestionAnswerRequest Choose(string option, string key = "")
        { return new QuestionAnswerRequest { Answers = new List<QuestionAnswerInput> { new QuestionAnswerInput { Key = "scope", Selected = new List<string> { option } } }, IdempotencyKey = key }; }

        private static async Task<string> TranscriptAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, CancellationToken token)
        {
            MessagePage page = await database.ReadMessagePageAsync(context, run.ConversationId, pageSize: 100, token: token).ConfigureAwait(false);
            return string.Join("\n", page.Items.Select(item => item.Kind + ": " + item.Content));
        }

        /// <summary>A held call returns the answer to the same turn; Answer later parks it and the answer resumes the step; pause, stop and restart behave as documented.</summary>
        internal static Task LifecycleAsync(CancellationToken token)
        {
            return UsingHomeAsync("lifecycle", async (database, context, home, cancel) =>
            {
                List<string> seats = await SeatsAsync(database, context, home, cancel).ConfigureAwait(false);

                // Held: answered while the call waits, the turn continues with the answer and nothing parks.
                QuestionFixtureRunner held = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, held))
                {
                    DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 60 }, "held", cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "held", cancel).ConfigureAwait(false);
                    run = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, cancel).ConfigureAwait(false);
                    Require(run.State == "awaiting_input", "A held question must show the run as waiting for input: " + run.State);
                    Require(open.AskerName == "Question agent 1" && open.AskerRole == "participant" && open.Phase == "response" && open.Questions.Single().Options.Count == 2, "The request must record the asking seat, role, phase and options.");
                    try { await engine.ApplyAsync(context, run.ConversationId, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "early" }, "fixture", cancel).ConfigureAwait(false); throw new InvalidDataException("A waiting run was resumed."); }
                    catch (DomainException error) when (error.Code == "invalid_state") { }
                    QuestionRequest answered = await engine.AnswerQuestionAsync(context, open.Id, Choose("prod", "held-answer"), cancel).ConfigureAwait(false);
                    Require(answered.Status == "answered", "The answer was not recorded.");
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(run.State == "completed", "The debate must finish after a held answer: " + run.State + "/" + run.Outcome);
                    Require(held.Results.Single().Contains("\"answered\"", StringComparison.Ordinal) && held.Results.Single().Contains("Production only", StringComparison.Ordinal), "The held call must return the answer: " + held.Results.Single());
                    string transcript = await TranscriptAsync(database, context, run, cancel).ConfigureAwait(false);
                    Require(transcript.Contains("Held answer: Production only", StringComparison.Ordinal), "The asking turn must use the answer in the same turn.");
                    QuestionRequest stored = await database.Questions.ReadAsync(context, open.Id, cancel).ConfigureAwait(false);
                    Require(stored.HoldState != "parked" && stored.ResumeAttemptId.Length == 0, "A held answer must not park or resume the turn.");
                    Require(run.WaitingSeconds >= 0 && run.AwaitingSinceUtc == null, "Waiting time must be recorded and the waiting mark cleared.");
                }

                // Answer later: the waiting turn ends now; the answer resumes the same step without a Resume command.
                QuestionFixtureRunner later = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, later))
                {
                    DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 60 }, "later", cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "held", cancel).ConfigureAwait(false);
                    await engine.DeferQuestionAsync(context, open.Id, cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, item => item.State == "awaiting_input" && later.Results.Count == 1, cancel).ConfigureAwait(false);
                    Require((await database.Questions.ReadAsync(context, open.Id, cancel).ConfigureAwait(false)).HoldState == "parked", "Answer later must park the question.");
                    Require(later.Results.Single().Contains("\"parked\"", StringComparison.Ordinal), "Answer later must return parked to the waiting call.");
                    Require(run.Phase == "response" && run.SpeakerIndex == 0, "A parked turn must not advance: " + run.Phase + "/" + run.SpeakerIndex);
                    QuestionRequest answered = await engine.AnswerQuestionAsync(context, open.Id, Choose("all"), cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(run.State == "completed", "The answer must resume the parked step by itself: " + run.State + "/" + run.Outcome);
                    QuestionRequest resumed = await database.Questions.ReadAsync(context, open.Id, cancel).ConfigureAwait(false);
                    Require(resumed.ResumeAttemptId.Length > 0, "The resume attempt must be linked.");
                    Require(later.Prompts.Any(prompt => prompt.Contains("You paused this step to ask the user", StringComparison.Ordinal)), "The resumed attempt must be told it is continuing after the answer.");
                    string transcript = await TranscriptAsync(database, context, run, cancel).ConfigureAwait(false);
                    Require(transcript.Contains("Resumed with: - Scope (Which environments?): Every environment", StringComparison.Ordinal) && transcript.Contains("answer: The user answered Question agent 1:", StringComparison.Ordinal), "The resumed turn must read the answer from the panel-visible answer message: " + transcript);
                    AttemptPage attempts = await database.Attempts.EnumerateAsync(context, run.ConversationId, run.Id, pageSize: 100, token: cancel).ConfigureAwait(false);
                    Require(attempts.Items.Count(item => item.State == "awaiting_input") == 1, "Exactly the parked attempt must be marked as waiting for input.");
                    try { await engine.AnswerQuestionAsync(context, open.Id, Choose("prod"), cancel).ConfigureAwait(false); throw new InvalidDataException("A second answer was accepted."); }
                    catch (DomainException error) when (error.Code == "question_already_resolved") { }
                }

                // Pause while held: the turn ends and the debate stays paused after the answer.
                QuestionFixtureRunner paused = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, paused))
                {
                    DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 60 }, "pause", cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "held", cancel).ConfigureAwait(false);
                    await engine.ApplyAsync(context, run.ConversationId, run.Id, new CommandRequest { Action = "pause", IdempotencyKey = "pause" }, "fixture", cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, item => item.State == "paused", cancel).ConfigureAwait(false);
                    try { await engine.ApplyAsync(context, run.ConversationId, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "resume-open" }, "fixture", cancel).ConfigureAwait(false); throw new InvalidDataException("Resume ignored an open question."); }
                    catch (DomainException error) when (error.Code == "question_pending") { }
                    await engine.AnswerQuestionAsync(context, open.Id, Choose("prod"), cancel).ConfigureAwait(false);
                    await Task.Delay(200, cancel).ConfigureAwait(false);
                    run = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, cancel).ConfigureAwait(false);
                    Require(run.State == "paused", "Answering a paused debate must not resume it: " + run.State);
                    await engine.ApplyAsync(context, run.ConversationId, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "resume" }, "fixture", cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, item => item.State == "completed" || item.State == "failed", cancel).ConfigureAwait(false);
                    Require(run.State == "completed", "Resume after the answer must finish the debate: " + run.State + "/" + run.Outcome);
                }

                // Stop while held: the run stops and its question is cancelled.
                QuestionFixtureRunner stopped = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, stopped))
                {
                    DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 60 }, "stop", cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "held", cancel).ConfigureAwait(false);
                    await engine.ApplyAsync(context, run.ConversationId, run.Id, new CommandRequest { Action = "stop", IdempotencyKey = "stop" }, "fixture", cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, item => item.State == "cancelled", cancel).ConfigureAwait(false);
                    QuestionRequest cancelled = await database.Questions.ReadAsync(context, open.Id, cancel).ConfigureAwait(false);
                    Require(cancelled.Status == "cancelled" && cancelled.ResolutionReason == "run_stopped", "Stopping must cancel the open question: " + cancelled.Status);
                }

                // The opt-in {answered_questions} placeholder lists answers in later prompts.
                QuestionFixtureRunner placeholder = new QuestionFixtureRunner();
                PromptTemplate withAnswers = await database.Prompts.CreateAsync(context, new PromptTemplate { Name = "Response with answers", Purpose = "response", Body = "You are {agent_name}. Respond to the orchestrator: {current_question}\nAnswers so far:\n{answered_questions}" }, cancel).ConfigureAwait(false);
                await using (ConversationEngine engine = new ConversationEngine(database, placeholder))
                {
                    Conversation conversation = await database.CreateConversationAsync(context, "Questions placeholder", cancel).ConfigureAwait(false);
                    DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Placeholder?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { Intake = "off", QuestionHoldSeconds = 60 }, PromptIds = new Dictionary<string, string> { ["response"] = withAnswers.Id }, IdempotencyKey = "run-placeholder" }, cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "held", cancel).ConfigureAwait(false);
                    await engine.AnswerQuestionAsync(context, open.Id, Choose("all"), cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(placeholder.Prompts.Any(prompt => prompt.Contains("You are Question agent 2", StringComparison.Ordinal) && prompt.Contains("The user answered Question agent 1: - Scope (Which environments?): Every environment", StringComparison.Ordinal)), "{answered_questions} must list the answer in a later seat's prompt.");
                    Require(!placeholder.Prompts.First(prompt => prompt.Contains("You are Question agent 1", StringComparison.Ordinal)).Contains("The user answered", StringComparison.Ordinal), "{answered_questions} must be empty before any answer.");
                }

                // Restart while held: the question parks, the run keeps waiting, and the answer resumes it under a new engine.
                QuestionFixtureRunner first = new QuestionFixtureRunner();
                DebateRun restarted;
                QuestionRequest waiting;
                await using (ConversationEngine engine = new ConversationEngine(database, first))
                {
                    restarted = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 60 }, "restart", cancel).ConfigureAwait(false);
                    waiting = await OpenAsync(database, context, restarted, "held", cancel).ConfigureAwait(false);
                }
                restarted = await database.Runs.ReadAsync(context, restarted.ConversationId, restarted.Id, cancel).ConfigureAwait(false);
                waiting = await database.Questions.ReadAsync(context, waiting.Id, cancel).ConfigureAwait(false);
                Require(restarted.State == "awaiting_input" && waiting.HoldState == "parked" && waiting.Status == "open", "A service stop while waiting must park the question and keep the run waiting: " + restarted.State + "/" + waiting.HoldState);
                QuestionFixtureRunner second = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, second))
                {
                    await engine.AnswerQuestionAsync(context, waiting.Id, Choose("prod"), cancel).ConfigureAwait(false);
                    restarted = await WaitAsync(database, context, restarted, Finished, cancel).ConfigureAwait(false);
                    Require(restarted.State == "completed", "The answer after a restart must resume the debate: " + restarted.State + "/" + restarted.Outcome);
                }
            }, token);
        }

        /// <summary>Waiting does not use the wall-clock budget or a capacity slot; the hold limit parks extra calls; expiry skips and resumes.</summary>
        internal static Task BudgetsAsync(CancellationToken token)
        {
            return UsingHomeAsync("budgets", async (database, context, home, cancel) =>
            {
                List<string> seats = await SeatsAsync(database, context, home, cancel).ConfigureAwait(false);

                // A three-second wall-clock budget survives a five-second wait for the answer.
                QuestionFixtureRunner slow = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, slow, capacity: 1))
                {
                    DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 60, MaxWallClockSeconds = 3 }, "wall-clock", cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "held", cancel).ConfigureAwait(false);
                    // With one run slot, a second debate still completes while the first waits for the answer.
                    DebateRun other = await engine.StartAsync(context, (await database.CreateConversationAsync(context, "Questions capacity", cancel).ConfigureAwait(false)).Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Other?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { UserQuestions = "off", Intake = "off" }, IdempotencyKey = "capacity" }, cancel).ConfigureAwait(false);
                    other = await WaitAsync(database, context, other, Finished, cancel).ConfigureAwait(false);
                    Require(other.State == "completed", "A waiting run must release its capacity slot: " + other.State + "/" + other.Outcome);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancel).ConfigureAwait(false);
                    await engine.AnswerQuestionAsync(context, open.Id, Choose("prod"), cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(run.State == "completed" && run.WaitingSeconds >= 2, "Waiting must not count against the wall-clock budget: " + run.State + "/" + run.Outcome + ", waited " + run.WaitingSeconds);
                }

                // With no hold slots, a waiting question parks at once.
                QuestionFixtureRunner limited = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, limited) { MaxLiveQuestionHolds = 0 })
                {
                    DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 60 }, "no-holds", cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "parked", cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, item => item.State == "awaiting_input" && limited.Results.Count == 1, cancel).ConfigureAwait(false);
                    Require(limited.Results.Single().Contains("\"parked\"", StringComparison.Ordinal), "Above the hold limit a question must park at once.");

                    // Skipping resumes the parked step, and the agent is told to continue on assumptions.
                    await engine.SkipQuestionAsync(context, open.Id, new QuestionSkipRequest { Note = "Assume production." }, cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(run.State == "completed", "Skipping a parked question must resume the debate: " + run.State + "/" + run.Outcome);
                    string skippedTranscript = await TranscriptAsync(database, context, run, cancel).ConfigureAwait(false);
                    Require(skippedTranscript.Contains("The user skipped", StringComparison.Ordinal) && skippedTranscript.Contains("Note from the user: Assume production.", StringComparison.Ordinal), "The panel must see the skip and its note.");
                }
                QuestionFixtureRunner expiring = new QuestionFixtureRunner();
                await using (ConversationEngine engine = new ConversationEngine(database, expiring))
                {
                    DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = 0, QuestionExpirySeconds = 1 }, "expiry", cancel).ConfigureAwait(false);
                    QuestionRequest open = await OpenAsync(database, context, run, "parked", cancel).ConfigureAwait(false);
                    Require(open.ExpiresUtc.HasValue, "An expiring question must record when it expires.");
                    await WaitAsync(database, context, run, item => item.State == "awaiting_input", cancel).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(1200), cancel).ConfigureAwait(false);
                    List<QuestionRequest> expired = await engine.ExpireQuestionsAsync(cancel).ConfigureAwait(false);
                    Require(expired.Any(item => item.Id == open.Id), "The due question must expire.");
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    QuestionRequest stored = await database.Questions.ReadAsync(context, open.Id, cancel).ConfigureAwait(false);
                    Require(run.State == "completed" && stored.Status == "expired" && stored.ResolutionReason == "expired", "An expired question must resume the debate as skipped: " + run.State + "/" + stored.Status);
                    string transcript = await TranscriptAsync(database, context, run, cancel).ConfigureAwait(false);
                    Require(transcript.Contains("The user did not answer in time", StringComparison.Ordinal), "The panel must be told the question expired.");
                }

                // Policy: off offers no tool; orchestrator-only hides it from participants; limits apply per turn and per debate.
                Conversation probeConversation = await database.CreateConversationAsync(context, "Question policy", cancel).ConfigureAwait(false);
                DebateRun probe = await database.Runs.CreateAsync(context, probeConversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Policy?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { Intake = "off" }, IdempotencyKey = "policy" }, cancel).ConfigureAwait(false);
                probe.Phase = "response"; probe.Policy.UserQuestions = "off";
                Require(QuestionTools.Definitions(probe).Count == 0, "userQuestions off must offer no ask_user tool.");
                probe.Policy.UserQuestions = "orchestrator";
                Require(QuestionTools.Definitions(probe).Count == 0, "Orchestrator-only must hide ask_user from participants.");
                probe.Phase = "question";
                Require(QuestionTools.Definitions(probe).Count == 1, "Orchestrator-only must offer ask_user to the orchestrator.");
                probe.Phase = "compaction"; probe.Policy.UserQuestions = "all";
                Require(QuestionTools.Definitions(probe).Count == 0, "Summaries must never ask the user.");
            }, token);
        }

        /// <summary>New debates open with an intake step: the orchestrator may ask about a vague question before round 1, within the intake limit, or start at once.</summary>
        internal static Task IntakeAsync(CancellationToken token)
        {
            return UsingHomeAsync("intake", async (database, context, home, cancel) =>
            {
                List<string> seats = await SeatsAsync(database, context, home, cancel).ConfigureAwait(false);
                Require(new DebatePolicy().Intake == "ask_if_needed", "New debates must default to an intake step.");
                QuestionFixtureRunner asking = new QuestionFixtureRunner { AskerName = "Question agent 0", AskDuringIntake = true };
                await using (ConversationEngine engine = new ConversationEngine(database, asking))
                {
                    Conversation conversation = await database.CreateConversationAsync(context, "Questions intake", cancel).ConfigureAwait(false);
                    DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Make deploys safer.", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { QuestionHoldSeconds = 60 }, IdempotencyKey = "run-intake" }, cancel).ConfigureAwait(false);
                    Require(run.Phase == "intake", "A new debate must start in the intake phase: " + run.Phase);
                    QuestionRequest open = await OpenAsync(database, context, run, "held", cancel).ConfigureAwait(false);
                    Require(open.Phase == "intake" && open.AskerRole == "orchestrator", "The intake question must come from the orchestrator in the intake phase: " + open.Phase + "/" + open.AskerRole);
                    QuestionRequest answered = await engine.AnswerQuestionAsync(context, open.Id, Choose("prod"), cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(run.State == "completed", "The debate must run its rounds after intake: " + run.State + "/" + run.Outcome);
                    AttemptPage attempts = await database.Attempts.EnumerateAsync(context, run.ConversationId, run.Id, pageSize: 100, token: cancel).ConfigureAwait(false);
                    List<GenerationAttempt> ordered = attempts.Items.OrderBy(item => item.Number).ToList();
                    Require(ordered[0].Purpose == "intake" && ordered[1].Purpose == "question", "Intake must come before the first question: " + string.Join(",", ordered.Select(item => item.Purpose)));
                    Require(ordered.Where(item => item.Purpose == "response").All(item => item.ContextMessageIds.Contains(answered.AnswerMessageId)), "Every seat must see the intake answer.");
                    Require(run.Usage.OrchestrationCalls >= 3, "Intake must count as an orchestration call.");
                }
                QuestionFixtureRunner clear = new QuestionFixtureRunner { AskerName = "Nobody", AskDuringIntake = true };
                await using (ConversationEngine engine = new ConversationEngine(database, clear))
                {
                    Conversation conversation = await database.CreateConversationAsync(context, "Questions intake clear", cancel).ConfigureAwait(false);
                    DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Use blue-green deploys for the billing service?", MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "run-clear" }, cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(run.State == "completed" && clear.Intakes == 1 && (await database.Questions.RunRequestsAsync(context, run.ConversationId, run.Id, cancel).ConfigureAwait(false)).Count == 0, "A clear question must start the panel without asking.");
                }
                QuestionFixtureRunner limited = new QuestionFixtureRunner { AskerName = "Question agent 0", AskDuringIntake = true, Arguments = JsonSerializer.Serialize(new { questions = new object[] { new { prompt = "One?", kind = "confirm" }, new { prompt = "Two?", kind = "confirm" }, new { prompt = "Three?", kind = "confirm" } } }) };
                await using (ConversationEngine engine = new ConversationEngine(database, limited))
                {
                    Conversation conversation = await database.CreateConversationAsync(context, "Questions intake limit", cancel).ConfigureAwait(false);
                    DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Improve it.", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { MaxIntakeQuestions = 2 }, IdempotencyKey = "run-limit" }, cancel).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, Finished, cancel).ConfigureAwait(false);
                    Require(limited.Results.Single().Contains("intake_question_limit", StringComparison.Ordinal), "Intake must refuse more questions than its limit: " + limited.Results.Single());
                }
            }, token);
        }

        /// <summary>Validation, storage shape, tenant isolation, first-writer-wins answers, credential warnings and the inbox.</summary>
        internal static Task StorageAsync(CancellationToken token)
        {
            return UsingHomeAsync("storage", (database, context, home, cancel) => StorageInDatabaseAsync(database, context, home, cancel), token);
        }

        /// <summary>The storage checks against an initialized database, shared with the PostgreSQL provider job.</summary>
        internal static async Task StorageInDatabaseAsync(DatabaseDriverBase database, RequestContext context, string home, CancellationToken token)
        {
            List<string> seats = await SeatsAsync(database, context, home, token).ConfigureAwait(false);
            Conversation conversation = await database.CreateConversationAsync(context, "Question storage", token).ConfigureAwait(false);
            DebateRun run = await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Storage?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { Intake = "off" }, IdempotencyKey = "storage" }, token).ConfigureAwait(false);
            QuestionAsk Ask(string call, params Question[] questions) { return new QuestionAsk { ConversationId = conversation.Id, RunId = run.Id, AttemptId = "attempt-" + call, CallId = call, AskerId = seats[1], AskerName = "Question agent 1", Phase = "response", Questions = questions.ToList(), MaxPerTurn = 2, MaxPerRun = 3 }; }
            Question Choice(bool multi = false) { return new Question { Key = "env", Header = "Environment", Prompt = "Which environment?", Kind = multi ? "multi_choice" : "single_choice", AllowOther = true, MaxSelections = multi ? 2 : 0, Options = new List<QuestionOption> { new QuestionOption { Key = "prod", Label = "Production", Recommended = true }, new QuestionOption { Key = "stage", Label = "Staging" }, new QuestionOption { Key = "dev", Label = "Development" } } }; }

            List<KeyValuePair<string, Question>> invalids = new List<KeyValuePair<string, Question>>
            {
                new KeyValuePair<string, Question>("questions[0].options", new Question { Prompt = "One option", Kind = "single_choice", Options = new List<QuestionOption> { new QuestionOption { Key = "a", Label = "A" } } }),
                new KeyValuePair<string, Question>("questions[0].kind", new Question { Prompt = "Unknown", Kind = "essay" }),
                new KeyValuePair<string, Question>("questions[0].prompt", new Question { Prompt = "", Kind = "text" }),
                new KeyValuePair<string, Question>("questions[0].options", new Question { Prompt = "Two recommended", Kind = "single_choice", Options = new List<QuestionOption> { new QuestionOption { Key = "a", Label = "A", Recommended = true }, new QuestionOption { Key = "b", Label = "B", Recommended = true } } }),
                new KeyValuePair<string, Question>("questions[0].maxSelections", new Question { Prompt = "Too many", Kind = "multi_choice", MaxSelections = 5, Options = new List<QuestionOption> { new QuestionOption { Key = "a", Label = "A" }, new QuestionOption { Key = "b", Label = "B" } } }),
            };
            for (int index = 0; index < invalids.Count; index++)
            {
                try { await database.Questions.CreateAsync(context, Ask("invalid-" + index, invalids[index].Value), token).ConfigureAwait(false); throw new InvalidDataException("Invalid question accepted: " + invalids[index].Key); }
                catch (DomainException error) when (error.Code == "invalid_question" && error.Field == invalids[index].Key) { }
            }
            try { await database.Questions.CreateAsync(context, Ask("five", Choice(), Choice(), Choice(), Choice(), Choice()), token).ConfigureAwait(false); throw new InvalidDataException("Five questions accepted."); }
            catch (DomainException error) when (error.Code == "invalid_question") { }

            QuestionAskResult created = await database.Questions.CreateAsync(context, Ask("one", Choice(), new Question { Key = "pain", Prompt = "Which failures?", Kind = "multi_choice", Options = Choice(true).Options, MaxSelections = 2 }, new Question { Key = "window", Prompt = "Rollback window? We never need your password.", Kind = "text", Required = false, MaxLength = 20 }, new Question { Key = "ok", Prompt = "Proceed?", Kind = "confirm" }), token).ConfigureAwait(false);
            QuestionRequest request = created.Request;
            Require(!created.Existing && request.Id.StartsWith("qrq_", StringComparison.Ordinal) && request.Questions.Count == 4 && request.Questions[0].Id.StartsWith("qst_", StringComparison.Ordinal) && request.Questions[0].Options[0].Id.StartsWith("qop_", StringComparison.Ordinal), "The request, questions and options must be stored with their IDs.");
            Require(request.Questions[0].Header == "Environment" && request.Questions[2].Header.Length > 0 && request.Questions[1].MinSelections == 1 && request.Questions[1].MaxSelections == 2, "Headers and selection bounds must be normalized.");
            Require(request.CredentialWarning, "A prompt mentioning a password must be flagged.");
            Require((await database.Questions.CreateAsync(context, Ask("one", Choice()), token).ConfigureAwait(false)).Existing, "Repeating a call ID must return the existing request.");
            await database.Questions.CreateAsync(context, Ask("two", Choice()), token).ConfigureAwait(false);
            try { await database.Questions.CreateAsync(context, Ask("three", Choice()), token).ConfigureAwait(false); await database.Questions.CreateAsync(context, Ask("four", Choice()), token).ConfigureAwait(false); throw new InvalidDataException("The per-debate limit was not enforced."); }
            catch (DomainException error) when (error.Code == "question_limit") { }

            Tenant tenant = await database.Identity.CreateTenantAsync(context, new TenantRequest { Name = "Question tenant " + Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
            RequestContext other = new RequestContext { TenantId = tenant.Id, UserId = "usr_question_outsider", IsAuthenticated = true, IsTenantAdmin = true, Role = "member" };
            try { await database.Questions.ReadAsync(other, request.Id, token).ConfigureAwait(false); throw new InvalidDataException("Another tenant read a question."); }
            catch (DomainException error) when (error.StatusCode == 404) { }
            try { await database.Questions.AnswerAsync(other, request.Id, new QuestionAnswerRequest(), token).ConfigureAwait(false); throw new InvalidDataException("Another tenant answered a question."); }
            catch (DomainException error) when (error.StatusCode == 404) { }
            Require((await database.Questions.EnumerateAsync(other, new QuestionFilter { Scope = "tenant", Status = "" }, token).ConfigureAwait(false)).TotalCount == 0 && (await database.Questions.SummaryAsync(other, token).ConfigureAwait(false)).OpenTenant == 0, "Another tenant must not see questions or counts.");

            QuestionAnswerInput Input(string key) { return new QuestionAnswerInput { Key = key }; }
            QuestionAnswerRequest Valid(string idempotency)
            {
                QuestionAnswerInput env = Input("env"); env.OtherText = "Canary";
                QuestionAnswerInput pain = Input("pain"); pain.Selected = new List<string> { "prod", "dev" };
                QuestionAnswerInput window = Input("window"); window.Skip = true;
                QuestionAnswerInput ok = Input("ok"); ok.Confirmed = true;
                return new QuestionAnswerRequest { Answers = new List<QuestionAnswerInput> { env, pain, window, ok }, IdempotencyKey = idempotency };
            }
            QuestionAnswerRequest tooMany = Valid("bad"); tooMany.Answers[1].Selected = new List<string> { "prod", "stage", "dev" };
            try { await database.Questions.AnswerAsync(context, request.Id, tooMany, token).ConfigureAwait(false); throw new InvalidDataException("Too many selections accepted."); }
            catch (DomainException error) when (error.Code == "invalid_answer" && error.Field == "answers[1].selected") { }
            QuestionAnswerRequest missing = Valid("bad"); missing.Answers.RemoveAt(3);
            try { await database.Questions.AnswerAsync(context, request.Id, missing, token).ConfigureAwait(false); throw new InvalidDataException("A required question was left out."); }
            catch (DomainException error) when (error.Code == "invalid_answer") { }

            // Two users answer at once: exactly one wins, the other gets a conflict.
            RequestContext colleague = new RequestContext { TenantId = context.TenantId, UserId = "usr_colleague", IsAuthenticated = true };
            Task<QuestionRequest>[] racing = { database.Questions.AnswerAsync(context, request.Id, Valid("first"), token), database.Questions.AnswerAsync(colleague, request.Id, Valid("second"), token) };
            int conflicts = 0;
            foreach (Task<QuestionRequest> attempt in racing)
            {
                try { await attempt.ConfigureAwait(false); }
                catch (DomainException error) when (error.Code == "question_already_resolved") { conflicts++; }
            }
            Require(conflicts == 1, "Exactly one concurrent answer must win: " + conflicts + " conflicts.");
            QuestionRequest answered = await database.Questions.ReadAsync(context, request.Id, token).ConfigureAwait(false);
            Require(answered.Status == "answered" && answered.Questions[0].Answer!.OtherText == "Canary" && answered.Questions[1].Answer!.SelectedLabels.SequenceEqual(new[] { "Production", "Development" }) && answered.Questions[2].Answer!.Skipped && answered.Questions[3].Answer!.Confirmed == true, "The stored answers must match what was sent.");
            Message message = await database.ReadMessageAsync(context, conversation.Id, answered.AnswerMessageId, token).ConfigureAwait(false);
            Require(message.Kind == "answer" && message.RecipientId == null && message.Content.Contains("Other: Canary", StringComparison.Ordinal) && message.Content.Contains("Production; Development", StringComparison.Ordinal), "The answer must reach the panel as a moderator message: " + message.Content);

            QuestionPage inbox = await database.Questions.EnumerateAsync(context, new QuestionFilter { Scope = "mine", Status = "", PageSize = 10 }, token).ConfigureAwait(false);
            Require(inbox.TotalCount == 3 && inbox.Items[0].Status == "open" && inbox.Items.Last().Status == "answered", "The inbox must list open requests first.");
            Require((await database.Questions.EnumerateAsync(context, new QuestionFilter { Scope = "mine", Status = "resolved" }, token).ConfigureAwait(false)).TotalCount == 1, "The resolved filter must exclude open requests.");
            QuestionSummary summary = await database.Questions.SummaryAsync(context, token).ConfigureAwait(false);
            Require(summary.OpenMine == 2 && summary.OpenTenant == 2 && summary.NewestId.Length > 0, "Open counts must cover the caller and the tenant.");
            QuestionEventPage start = await database.Questions.ReadEventsAsync(context, "", 10, token).ConfigureAwait(false);
            Require(start.Items.Count == 0 && start.Next.Length > 0, "An empty cursor must return the newest cursor and no changes.");
            QuestionEventPage replay = await database.Questions.ReadEventsAsync(context, "0|", 100, token).ConfigureAwait(false);
            Require(replay.Items.Any(item => item.RequestId == request.Id && item.Status == "opened") && replay.Items.Any(item => item.RequestId == request.Id && item.Status == "answered"), "The change feed must report opened and answered.");
            // Deleting: never an open question, never while the debate runs, never across tenants; the answer message stays.
            string openId = inbox.Items[0].Id;
            Require(!answered.Deletable && answered.RunState == run.State, "A resolved question of a running debate must not be deletable: " + answered.RunState);
            try { await database.Questions.DeleteAsync(context, openId, token).ConfigureAwait(false); throw new InvalidDataException("An open question was deleted."); }
            catch (DomainException error) when (error.StatusCode == 409 && error.Code == "question_open") { }
            try { await database.Questions.DeleteAsync(context, request.Id, token).ConfigureAwait(false); throw new InvalidDataException("A question of a running debate was deleted."); }
            catch (DomainException error) when (error.StatusCode == 409 && error.Code == "active_run") { }
            Require((await database.Questions.CancelOpenAsync(context, run.Id, token).ConfigureAwait(false)) == 2, "Stopping must cancel the run's open requests.");
            run = await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
            run.State = "cancelled"; run.Outcome = "stopped";
            await database.Runs.SaveAsync(context, run, token).ConfigureAwait(false);
            QuestionRequest finished = await database.Questions.ReadAsync(context, request.Id, token).ConfigureAwait(false);
            Require(finished.Deletable && finished.RunState == "cancelled" && (await database.Questions.ReadAsync(context, openId, token).ConfigureAwait(false)).Deletable, "Resolved questions of a finished debate, including cancelled ones, must be deletable.");
            try { await database.Questions.DeleteAsync(other, request.Id, token).ConfigureAwait(false); throw new InvalidDataException("Another tenant deleted a question."); }
            catch (DomainException error) when (error.StatusCode == 404) { }
            await database.Questions.DeleteAsync(context, request.Id, token).ConfigureAwait(false);
            try { await database.Questions.ReadAsync(context, request.Id, token).ConfigureAwait(false); throw new InvalidDataException("A deleted question can still be read."); }
            catch (DomainException error) when (error.StatusCode == 404) { }
            Require((await database.ReadMessageAsync(context, conversation.Id, answered.AnswerMessageId, token).ConfigureAwait(false)).Content == message.Content, "Deleting a question must keep its answer message in the conversation.");
            Require((await database.Questions.EnumerateAsync(context, new QuestionFilter { Scope = "mine", Status = "", PageSize = 10 }, token).ConfigureAwait(false)).TotalCount == 2, "A deleted question must leave the inbox.");
            Require((await database.Questions.ReadEventsAsync(context, "0|", 100, token).ConfigureAwait(false)).Items.Any(item => item.RequestId == request.Id && item.Status == "deleted"), "The change feed must report deleted.");
            try { await database.Questions.DeleteAsync(context, request.Id, token).ConfigureAwait(false); throw new InvalidDataException("Deleting twice succeeded."); }
            catch (DomainException error) when (error.StatusCode == 404) { }
            await database.DeleteConversationAsync(context, conversation.Id, token).ConfigureAwait(false);
            try { await database.Questions.ReadAsync(context, request.Id, token).ConfigureAwait(false); throw new InvalidDataException("A question outlived its conversation."); }
            catch (DomainException error) when (error.StatusCode == 404) { }
        }

        /// <summary>A schema 34 database with clarification requests upgrades to questions: open blocking requests park and keep their runs waiting, answers keep their text, and the old table is gone.</summary>
        internal static async Task MigrationAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-questions-migration-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                RequestContext context = Context();
                DebateRun run;
                Message answer;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    List<string> seats = await SeatsAsync(database, context, home, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(context, "Clarifications before 2.3", token).ConfigureAwait(false);
                    run = await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Legacy?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { Intake = "off" }, IdempotencyKey = "legacy" }, token).ConfigureAwait(false);
                    answer = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, RunId = run.Id, Content = "Five percent first.", Kind = "interjection" }, "legacy-answer", token).ConfigureAwait(false);
                }
                // Turn the database back into schema 34 with clarification rows, as a 2.2 service left it.
                using (Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + paths.DatabaseFile + ";Pooling=False"))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand())
                    {
                        string now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                        command.CommandText = "DELETE FROM schema_migrations WHERE version=35; DROP TABLE question_answer_selections; DROP TABLE question_answers; DROP TABLE question_options; DROP TABLE questions; DROP TABLE question_requests; DROP TABLE attempt_continuations; "
                            + "CREATE TABLE debate_clarifications(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, conversation_id TEXT NOT NULL, run_id TEXT NOT NULL, profile_id TEXT NOT NULL, call_id TEXT NOT NULL, question TEXT NOT NULL, blocking INTEGER NOT NULL, status TEXT NOT NULL DEFAULT 'open', answer_message_id TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(run_id,call_id)); "
                            + "INSERT INTO debate_clarifications VALUES('clr_open','ten_local','" + run.ConversationId + "','" + run.Id + "','" + run.Participants[0].Id + "','a:1','What is the rollout limit?',1,'open','',1,'" + now + "','" + now + "'); "
                            + "INSERT INTO debate_clarifications VALUES('clr_done','ten_local','" + run.ConversationId + "','" + run.Id + "','" + run.Participants[0].Id + "','a:2','Which region?',1,'answered','" + answer.Id + "',1,'" + now + "','" + now + "'); "
                            + "INSERT INTO debate_clarifications VALUES('clr_skip','ten_local','" + run.ConversationId + "','" + run.Id + "','" + run.Participants[1].Id + "','a:3','Any budget?',0,'skipped','',1,'" + now + "','" + now + "'); "
                            + "UPDATE runs SET state='paused',outcome='awaiting_clarification' WHERE id='" + run.Id + "';";
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    List<QuestionRequest> migrated = await database.Questions.RunRequestsAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                    QuestionRequest open = migrated.Single(item => item.Id == "qrq_open");
                    QuestionRequest done = migrated.Single(item => item.Id == "qrq_done");
                    QuestionRequest skipped = migrated.Single(item => item.Id == "qrq_skip");
                    Require(migrated.Count == 3 && open.Status == "open" && open.Wait && open.HoldState == "parked" && open.Questions.Single().Kind == "text" && open.Questions.Single().Prompt == "What is the rollout limit?", "The open blocking clarification must become a parked text question.");
                    Require(done.Status == "answered" && done.Questions.Single().Answer!.Text == "Five percent first." && done.AnswerMessageId == answer.Id, "An answered clarification must keep its answer text and message.");
                    Require(skipped.Status == "skipped" && !skipped.Wait && skipped.ResolutionReason == "skipped_by_user" && skipped.Questions.Single().Answer!.Skipped, "A skipped clarification must stay skipped.");
                    DebateRun upgraded = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                    Require(upgraded.State == "awaiting_input" && upgraded.Outcome.Length == 0, "A run waiting on a clarification must wait for input after the upgrade: " + upgraded.State + "/" + upgraded.Outcome);
                    using (Microsoft.Data.Sqlite.SqliteConnection connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + paths.DatabaseFile + ";Pooling=False"))
                    {
                        await connection.OpenAsync(token).ConfigureAwait(false);
                        using (Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='debate_clarifications'";
                            Require(Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0, "The clarification table must be dropped.");
                        }
                    }
                    await using (ConversationEngine engine = new ConversationEngine(database, new MultiAgentFixtureRunner()))
                    {
                        await engine.AnswerQuestionAsync(context, open.Id, new QuestionAnswerRequest { Answers = new List<QuestionAnswerInput> { new QuestionAnswerInput { Key = "question", Text = "Ten percent." } } }, token).ConfigureAwait(false);
                        upgraded = await WaitAsync(database, context, upgraded, Finished, token).ConfigureAwait(false);
                        Require(upgraded.State == "completed", "Answering a migrated question must resume its debate: " + upgraded.State + "/" + upgraded.Outcome);
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        /// <summary>Claude Code and Codex over the MCP bridge and a generic CLI over the JSON envelope ask the user and receive the held answer.</summary>
        internal static Task TransportAsync(CancellationToken token)
        {
            return UsingHomeAsync("transport", async (database, context, home, cancel) =>
            {
                string fixture = Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!;
                string server = fixture.Replace("Test.Fixtures", "ArgueBot.Server", StringComparison.Ordinal);
                List<string> seats = await SeatsAsync(database, context, home, cancel).ConfigureAwait(false);
                Conversation conversation = await database.CreateConversationAsync(context, "Question transports", cancel).ConfigureAwait(false);
                DebateRun run = await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Transport?", MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { Intake = "off" }, IdempotencyKey = "transport" }, cancel).ConfigureAwait(false);
                run.Phase = "response";
                foreach (string mode in new[] { "mcp-questions-claude", "mcp-questions-codex", "envelope-questions" })
                {
                    string provider = mode == "mcp-questions-claude" ? "claude" : mode == "mcp-questions-codex" ? "codex" : "plain";
                    RunnerProfile profile = new RunnerProfile { Kind = "cli", Provider = provider, Executable = "dotnet", Arguments = new List<string> { fixture, mode }, TimeoutSeconds = 30 };
                    profile.RuntimeMcpCommand.AddRange(new[] { "dotnet", server });
                    string option = provider == "plain" ? "all" : "prod";
                    // The service holds the call; here the user answers at once, so the call returns the answer.
                    async Task<string> Answer(QuestionRequest request, CancellationToken wait)
                    {
                        await database.Questions.AnswerAsync(context, request.Id, Choose(option), wait).ConfigureAwait(false);
                        return QuestionCoordinator.Resolved;
                    }
                    ConversationTools tools = new ConversationTools(database, context, run, seats[1], long.MaxValue, "transport-" + mode, Answer);
                    StringBuilder output = new StringBuilder();
                    await new RunnerRouter(true).GenerateWithContextAsync(profile, "Ask the user.", new List<Message>(), ConversationTools.Definitions(run), tools.ExecuteAsync, false, (text, _) => { output.Append(text); return Task.CompletedTask; }, (_, _) => Task.CompletedTask, cancel).ConfigureAwait(false);
                    string expected = provider == "plain" ? "Envelope question answered: Every environment." : "MCP question answered: Production only.";
                    Require(output.ToString().Trim() == expected, mode + " did not receive the held answer: " + output);
                }
                run.Policy.UserQuestions = "off";
                StringBuilder none = new StringBuilder();
                RunnerProfile plain = new RunnerProfile { Kind = "cli", Provider = "plain", Executable = "dotnet", Arguments = new List<string> { fixture, "envelope-questions" }, TimeoutSeconds = 30 };
                await new RunnerRouter(true).GenerateWithContextAsync(plain, "Ask the user.", new List<Message>(), ConversationTools.Definitions(run), new ConversationTools(database, context, run, seats[1], long.MaxValue, "transport-off").ExecuteAsync, false, (text, _) => { none.Append(text); return Task.CompletedTask; }, (_, _) => Task.CompletedTask, cancel).ConfigureAwait(false);
                Require(none.ToString().Trim() == "No question tool offered.", "userQuestions off must not advertise ask_user over the envelope.");
            }, token);
        }

        /// <summary>REST models ask through native function calls: a held answer returns to the same turn, and a parked turn resumes after the answer.</summary>
        internal static Task RestAsync(CancellationToken token)
        {
            return UsingHomeAsync("rest", async (database, context, home, cancel) =>
            {
                EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "Question simulation", Protocol = "ollama", BaseUrl = Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL")!, TimeoutSeconds = 20 }, cancel).ConfigureAwait(false);
                ModelProfile asking = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "Asking model", EndpointId = endpoint.Id, Model = "multi-agent-questions", SupportsTools = true }, cancel).ConfigureAwait(false);
                ModelProfile plain = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "Panel model", EndpointId = endpoint.Id, Model = "multi-agent", SupportsTools = true }, cancel).ConfigureAwait(false);
                List<string> seats = new List<string>
                {
                    (await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "REST orchestrator", ModelProfileId = plain.Id }, cancel).ConfigureAwait(false)).Id,
                    (await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "REST asker", ModelProfileId = asking.Id }, cancel).ConfigureAwait(false)).Id,
                    (await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "REST panelist", ModelProfileId = plain.Id }, cancel).ConfigureAwait(false)).Id,
                };
                using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, cancel).ConfigureAwait(false))
                await using (ConversationEngine engine = new ConversationEngine(database, new CredentialRunner(false, vault, database, context, Path.Combine(home, "runtime"))))
                {
                    foreach (int hold in new[] { 60, 0 })
                    {
                        DebateRun run = await StartAsync(engine, database, context, seats, new DebatePolicy { QuestionHoldSeconds = hold }, "rest-" + hold, cancel).ConfigureAwait(false);
                        QuestionRequest open = await OpenAsync(database, context, run, hold > 0 ? "held" : "parked", cancel).ConfigureAwait(false);
                        if (hold == 0) await WaitAsync(database, context, run, item => item.State == "awaiting_input", cancel).ConfigureAwait(false);
                        await engine.AnswerQuestionAsync(context, open.Id, Choose("prod"), cancel).ConfigureAwait(false);
                        run = await WaitAsync(database, context, run, Finished, cancel, 30).ConfigureAwait(false);
                        Require(run.State == "completed", "The REST debate must finish after the answer: " + run.State + "/" + run.Outcome);
                        string transcript = await TranscriptAsync(database, context, run, cancel).ConfigureAwait(false);
                        Require(transcript.Contains("Using your answer (Production only)", StringComparison.Ordinal), "The REST agent must use the answer (hold " + hold + "): " + transcript);
                        QuestionRequest stored = await database.Questions.ReadAsync(context, open.Id, cancel).ConfigureAwait(false);
                        Require(hold > 0 ? stored.ResumeAttemptId.Length == 0 : stored.ResumeAttemptId.Length > 0, "Only the parked turn resumes in a new attempt (hold " + hold + ").");
                    }
                }
            }, token);
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
