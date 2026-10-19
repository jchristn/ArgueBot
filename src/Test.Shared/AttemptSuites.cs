namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using Touchstone.Core;

    /// <summary>Attempt durability, prompt snapshots, recipient boundaries and explicit retry behavior.</summary>
    public static class AttemptSuites
    {
        /// <summary>Shared attempt descriptors run through every test adapter.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new[] { new TestSuiteDescriptor("Attempts", "Generation provenance", new[] { new TestCaseDescriptor("Attempts", "ContextBudget", "Over-budget history pauses before any external invocation or answer acceptance", ContextBudgetAsync), new TestCaseDescriptor("Attempts", "FinalPause", "Pause after a final answer finishes cleanly and exhausted legacy runs resume without another invocation", FinalPauseAsync), new TestCaseDescriptor("Attempts", "Storage", "Atomic attempt completion, private context, budgets, crash reconciliation and terminal retries", StorageAsync), new TestCaseDescriptor("Attempts", "DirectiveRetry", "Interrupted directives survive explicit retry and frozen prompts survive conversation rename", DirectiveRetryAsync) }) }; }
        }
        private static RequestContext Owner() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" }; }
        private static async Task ContextBudgetAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-context-budget-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false); RequestContext owner = Owner();
                    RunnerProfile profile = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Budget fixture", Executable = "fixture-not-executed" }, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(owner, "Budget fixture", token).ConfigureAwait(false);
                    await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = new string('x', 2048) }, token: token).ConfigureAwait(false);
                    FinalPauseRunner runner = new FinalPauseRunner();
                    await using (ConversationEngine engine = new ConversationEngine(database, runner, contextMaxCharacters: 1024))
                    {
                        DebateRun run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Budget-bound question", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "budget-followup" }, token).ConfigureAwait(false);
                        await WaitForAsync(database, owner, run, "paused", token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, token).ConfigureAwait(false);
                        if (run.Outcome != "context_budget_exceeded" || runner.Calls != 0 || (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).TotalCount != 0 || (await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Count != 2) throw new InvalidDataException("The context budget invoked a model, dropped history, created an answer or hid its recovery reason.");
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task FinalPauseAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-final-pause-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false); RequestContext owner = Owner();
                    RunnerProfile profile = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Final answer fixture", Executable = "fixture-not-executed" }, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(owner, "Final turn fixture", token).ConfigureAwait(false);
                    FinalPauseRunner runner = new FinalPauseRunner();
                    await using (ConversationEngine engine = new ConversationEngine(database, runner))
                    {
                        DebateRun run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "One final answer", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "final-followup" }, token).ConfigureAwait(false);
                        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, conversation.Id, run.Id, new CommandRequest { Action = "pause", IdempotencyKey = "late-pause" }, "fixture", token).ConfigureAwait(false);
                        runner.Complete(); await WaitForAsync(database, owner, run, "completed", token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, token).ConfigureAwait(false);
                        if (run.Outcome != "max_rounds" || run.Round != 2) throw new InvalidDataException("A late pause stranded an exhausted run.");
                        run.State = "paused"; await database.Runs.SaveAsync(owner, run, token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, conversation.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "legacy-exhausted-resume" }, "fixture", token).ConfigureAwait(false);
                        await WaitForAsync(database, owner, run, "completed", token).ConfigureAwait(false);
                        if (runner.Calls != 1 || (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).TotalCount != 1) throw new InvalidDataException("An exhausted legacy run invoked another external operation.");
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task StorageAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-attempt-storage-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                { await database.InitializeAsync(token).ConfigureAwait(false); await AttemptStorageChecks.RunAsync(database, Owner(), token).ConfigureAwait(false); }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task WaitForAsync(SqliteDatabaseDriver database, RequestContext context, DebateRun run, string state, CancellationToken token)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                while ((await database.Runs.ReadAsync(context, run.ConversationId, run.Id, deadline.Token).ConfigureAwait(false)).State != state) await Task.Delay(10, deadline.Token).ConfigureAwait(false);
            }
        }
        private static async Task DirectiveRetryAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-attempt-engine-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); RequestContext owner = Owner();
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RunnerProfile one = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "First", Executable = "fixture-not-executed" }, token).ConfigureAwait(false);
                    RunnerProfile two = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Second", Executable = "fixture-not-executed" }, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(owner, "Original immutable question 議論", token).ConfigureAwait(false);
                    CreateRunRequest request = new CreateRunRequest { ParticipantIds = new List<string> { one.Id, two.Id }, MaxRounds = 2, InterventionSeconds = 0, IdempotencyKey = "fixture-run" };
                    DebateRun run = await database.Runs.CreateAsync(owner, conversation.Id, request, token).ConfigureAwait(false);
                    Message directive = await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, RunId = run.Id, Kind = "steer", RecipientId = run.Participants[0].Id, Content = "Preserve this private instruction across an interrupted attempt." }, token: token).ConfigureAwait(false);
                    AttemptFixtureRunner runner = new AttemptFixtureRunner();
                    await using (ConversationEngine engine = new ConversationEngine(database, runner))
                    {
                        await engine.StartAsync(owner, conversation.Id, request, token).ConfigureAwait(false);
                        await runner.FirstStarted.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                        if ((await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Single(message => message.Id == directive.Id).State != "completed") throw new InvalidDataException("A directive was consumed before its answer completed.");
                        await engine.ApplyAsync(owner, conversation.Id, run.Id, new CommandRequest { Action = "interrupt", IdempotencyKey = "fixture-interrupt" }, "fixture-client", token).ConfigureAwait(false);
                        await WaitForAsync(database, owner, run, "paused", token).ConfigureAwait(false);
                        if ((await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Single(message => message.Id == directive.Id).State != "completed") throw new InvalidDataException("An interrupted answer lost its directive.");
                        Conversation current = await database.ReadConversationAsync(owner, conversation.Id, token).ConfigureAwait(false);
                        await database.UpdateConversationAsync(owner, conversation.Id, new ConversationRequest { Title = "Renamed display title", Revision = current.Revision }, token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, conversation.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "fixture-resume" }, "fixture-client", token).ConfigureAwait(false);
                        await WaitForAsync(database, owner, run, "completed", token).ConfigureAwait(false);
                    }
                    string[][] contexts = runner.Contexts.ToArray();
                    if (contexts.Length != 5 || !contexts[0].Contains(directive.Id) || !contexts[1].Contains(directive.Id) || contexts.Skip(2).Any(ids => ids.Contains(directive.Id)) || runner.Prompts.Any(prompt => !prompt.Contains(conversation.Title, StringComparison.Ordinal) || prompt.Contains("Renamed display title", StringComparison.Ordinal))) throw new InvalidDataException("Retry lost a directive, exposed it to another turn or changed a frozen question.");
                    AttemptPage attempts = await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                    if (attempts.TotalCount != 5 || attempts.Items.Count(item => item.State == "completed") != 4 || attempts.Items.Single(item => item.Number == 1).State != "interrupted" || attempts.Items.Any(item => item.PromptDigest.Length != 64 || item.DurationMilliseconds < 0)) throw new InvalidDataException("Durable attempt accounting does not match actual external invocations.");
                    string snapshot = Path.Combine(home, "attempts.jsonl");
                    await database.ExportSnapshotAsync(snapshot, token).ConfigureAwait(false);
                    using (SqliteDatabaseDriver restored = new SqliteDatabaseDriver(new ArgueBotPaths(Path.Combine(home, "restored"))))
                    {
                        await restored.InitializeAsync(token).ConfigureAwait(false); await restored.RestoreSnapshotAsync(snapshot, token).ConfigureAwait(false); await restored.InitializeAsync(token).ConfigureAwait(false);
                        AttemptPage recovered = await restored.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                        if (recovered.TotalCount != 5 || recovered.Items.Single(item => item.Number == 1).ContextMessageIds.Single() != directive.Id || (await restored.Runs.ReadAsync(owner, conversation.Id, run.Id, token).ConfigureAwait(false)).Question != conversation.Title) throw new InvalidDataException("Portable restoration lost execution provenance or the frozen question.");
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
