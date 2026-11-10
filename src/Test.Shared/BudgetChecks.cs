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
    using System.Threading;
    using System.Threading.Tasks;

    internal static class BudgetChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-budgets-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await RunInDatabaseAsync(database, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        internal static async Task RunInDatabaseAsync(DatabaseDriverBase database, CancellationToken token)
        {
            RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
            List<string> ids = new List<string>();
            for (int index = 0; index < 3; index++) ids.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Budget agent " + index, Executable = "fictional" }, token).ConfigureAwait(false)).Id);
            foreach (string scenario in new[] { "participant", "orchestrator", "estimate", "deadline", "success" })
            {
                DebatePolicy policy = new DebatePolicy { QuestionInstructions = "Compare the rollback evidence.", MaxToolCalls = 1, Intake = "off" };
                if (scenario == "participant") policy.MaxParticipantTurns = 1;
                if (scenario == "orchestrator") policy.MaxOrchestrationCalls = 1;
                if (scenario == "estimate") policy.MaxEstimatedInputTokens = 1;
                if (scenario == "deadline") policy.MaxWallClockSeconds = 1;
                Conversation conversation = await database.CreateConversationAsync(context, "Budget " + scenario, token).ConfigureAwait(false);
                MultiAgentFixtureRunner runner = new MultiAgentFixtureRunner { HoldPhase = scenario == "deadline" ? "question" : "" };
                await using (ConversationEngine engine = new ConversationEngine(database, runner))
                {
                    DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, ParticipantIds = ids.Take(2).ToList(), OrchestratorId = ids[2], MaxRounds = 1, InterventionSeconds = 0, Policy = policy, IdempotencyKey = "budget" }, token).ConfigureAwait(false);
                    run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                    string expected = scenario == "success" ? "consensus" : scenario == "deadline" ? "wall_clock_budget_exhausted" : "debate_budget_exhausted";
                    if (run.Outcome != expected || run.Policy.QuestionInstructions != policy.QuestionInstructions) throw new InvalidDataException("Incorrect budget outcome or frozen policy: " + scenario + " / " + run.Outcome);
                    if (scenario == "participant" && run.Usage.ParticipantTurns != 1 || scenario == "orchestrator" && run.Usage.OrchestrationCalls != 1 || scenario == "estimate" && run.Usage.OrchestrationCalls != 0) throw new InvalidDataException("A phase budget was exceeded or charged after refusal.");
                    if (scenario == "success" && (run.Usage.ParticipantTurns != 4 || run.Usage.OrchestrationCalls != 2 || run.Usage.EstimatedInputTokens <= 0)) throw new InvalidDataException("Successful debate usage lost independent counters.");
                    if (scenario == "participant")
                    {
                        await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "retry-budget" }, "fixture", token).ConfigureAwait(false);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        if (run.Usage.ParticipantTurns != 1 || run.Outcome != expected) throw new InvalidDataException("Resume reset a budget.");
                    }
                    if (scenario == "success")
                    {
                        for (int index = 0; index < 2; index++)
                        {
                            await database.Budgets.ReserveToolAsync(context, run.Id, "fixture-attempt", "same-call", token).ConfigureAwait(false);
                        }
                        run = await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                        if (run.Usage.ToolCalls != 1) throw new InvalidDataException("A repeated tool identity was charged twice.");
                        try { await database.Budgets.ReserveToolAsync(context, run.Id, "fixture-attempt", "new-call", token).ConfigureAwait(false); throw new InvalidDataException("Tool limit did not refuse a new call."); }
                        catch (DomainException failure) when (failure.Code == "tool_budget_exhausted") { }
                    }
                }
            }
        }
        private static async Task<DebateRun> WaitAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, CancellationToken token)
        {
            for (int count = 0; count < 1000; count++)
            {
                await Task.Delay(10, token).ConfigureAwait(false);
                run = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, token).ConfigureAwait(false);
                if (run.State == "paused" || run.State == "completed") return run;
            }
            throw new TimeoutException("Budget fixture did not settle.");
        }
    }
}
