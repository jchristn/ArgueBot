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

    /// <summary>Reusable isolated persistence behavior suites.</summary>
    public static class StorageSuites
    {
        /// <summary>All storage descriptors. No live home, provider, or paid API is touched.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Storage", "Durable conversations", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Storage", "RoundTrip", "History, receipts, search, backup and tenant isolation", RoundTripAsync),
                    new TestCaseDescriptor("Storage", "RunRecovery", "Immutable snapshots and interrupted run recovery", RunRecoveryAsync),
                    new TestCaseDescriptor("Storage", "ExclusiveHome", "Exclusive application-home ownership", ExclusiveHomeAsync),
                    new TestCaseDescriptor("Storage", "Engine", "Service-owned execution, interruption and resumption", EngineAsync)
                }) };
            }
        }
        private static RequestContext Owner() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" }; }
        private static async Task RoundTripAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); RequestContext context = Owner(); string id;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(context, "Unicode 議論 ' % _", token).ConfigureAwait(false); id = conversation.Id;
                    Message first = await database.AppendMessageAsync(context, new Message { ConversationId = id, Content = "persistent needle", Kind = "note" }, "same", token).ConfigureAwait(false);
                    Message repeated = await database.AppendMessageAsync(context, new Message { ConversationId = id, Content = "persistent needle", Kind = "note" }, "same", token).ConfigureAwait(false);
                    if (first.Id != repeated.Id) throw new InvalidDataException("Repeated command created a second message.");
                    try { await database.AppendMessageAsync(context, new Message { ConversationId = id, Content = "different" }, "same", token).ConfigureAwait(false); throw new InvalidDataException("Conflicting command key accepted."); } catch (DomainException ex) when (ex.StatusCode == 409) { }
                    Page page = await database.EnumerateConversationsAsync(context, "needle", token: token).ConfigureAwait(false);
                    if (page.TotalCount != 1) throw new InvalidDataException("Search did not find message content.");
                    RequestContext foreign = Owner(); foreign.TenantId = "ten_foreign";
                    try { await database.ReadConversationAsync(foreign, id, token).ConfigureAwait(false); throw new InvalidDataException("Cross-tenant read accepted."); } catch (DomainException ex) when (ex.StatusCode == 404) { }
                    RequestContext viewer = Owner(); viewer.Role = "viewer";
                    try { await database.CreateConversationAsync(viewer, "denied", token).ConfigureAwait(false); throw new InvalidDataException("Viewer mutation accepted."); } catch (DomainException ex) when (ex.StatusCode == 403) { }
                    List<DurableEvent> events = await database.ReadEventsAsync(context, id, token: token).ConfigureAwait(false);
                    if (events.Count != 2 || events[0].Sequence != 1 || events[1].Sequence != 2) throw new InvalidDataException("Durable event sequence or deduplication failed.");
                    await database.BackupAsync(Path.Combine(home, "backups", "test.db"), token).ConfigureAwait(false);
                }
                using (SqliteDatabaseDriver reopened = new SqliteDatabaseDriver(paths))
                {
                    await reopened.InitializeAsync(token).ConfigureAwait(false);
                    List<Message> messages = await reopened.ReadMessagesAsync(context, id, token).ConfigureAwait(false);
                    if (messages.Count != 1 || messages[0].Content != "persistent needle") throw new InvalidDataException("History did not survive restart.");
                    Conversation current = await reopened.ReadConversationAsync(context, id, token).ConfigureAwait(false);
                    try { await reopened.UpdateConversationAsync(context, id, new ConversationRequest { Title = "stale", Revision = current.Revision - 1 }, token).ConfigureAwait(false); throw new InvalidDataException("Stale revision accepted."); } catch (DomainException ex) when (ex.StatusCode == 409) { }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task RunRecoveryAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); RequestContext context = Owner(); string conversationId; string runId;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(context, "recovery", token).ConfigureAwait(false); conversationId = conversation.Id;
                    RunnerProfile one = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "one", Executable = "fixture", Arguments = new List<string> { "a b", "議論" } }, token).ConfigureAwait(false);
                    RunnerProfile two = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "two", Executable = "fixture" }, token).ConfigureAwait(false);
                    CreateRunRequest request = new CreateRunRequest { ParticipantIds = new List<string> { one.Id, two.Id }, IdempotencyKey = "run" };
                    DebateRun run = await database.Runs.CreateAsync(context, conversationId, request, token).ConfigureAwait(false); runId = run.Id;
                    DebateRun repeated = await database.Runs.CreateAsync(context, conversationId, request, token).ConfigureAwait(false);
                    if (run.Id != repeated.Id || run.Participants[0].Id == one.Id || !run.Participants[0].Arguments.SequenceEqual(one.Arguments)) throw new InvalidDataException("Snapshot or run idempotency failed.");
                    run.State = "agent_turn"; await database.Runs.SaveAsync(context, run, token).ConfigureAwait(false);
                    await database.AppendMessageAsync(context, new Message { ConversationId = conversationId, RunId = run.Id, Content = "partial", State = "streaming", Speaker = "one" }, token: token).ConfigureAwait(false);
                }
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    DebateRun run = await database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
                    List<Message> messages = await database.ReadMessagesAsync(context, conversationId, token).ConfigureAwait(false);
                    if (run.State != "paused" || run.Outcome != "interrupted" || messages.Single().State != "interrupted") throw new InvalidDataException("Recovery did not pause uncertain work.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task EngineAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = Owner();
                    Conversation conversation = await database.CreateConversationAsync(context, "engine", token).ConfigureAwait(false);
                    RunnerProfile one = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "one", Executable = "fixture" }, token).ConfigureAwait(false);
                    RunnerProfile two = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "two", Executable = "fixture" }, token).ConfigureAwait(false);
                    FixtureRunner runner = new FixtureRunner { Wait = true };
                    ConversationEngine engine = new ConversationEngine(database, runner);
                    try
                    {
                        DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ParticipantIds = new List<string> { one.Id, two.Id }, MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "engine-run" }, token).ConfigureAwait(false);
                        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                        await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "interrupt" }, "test-client", token).ConfigureAwait(false);
                        run = await WaitStateAsync(database, context, conversation.Id, run.Id, "paused", token).ConfigureAwait(false);
                        if (runner.Calls != 1) throw new InvalidDataException("Interrupted generation was duplicated.");
                        runner.Wait = false;
                        await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "resume", Revision = run.Revision }, "test-client", token).ConfigureAwait(false);
                        run = await WaitStateAsync(database, context, conversation.Id, run.Id, "completed", token).ConfigureAwait(false);
                        List<Message> messages = await database.ReadMessagesAsync(context, conversation.Id, token).ConfigureAwait(false);
                        if (run.Outcome != "max_rounds" || messages.Count(item => item.State == "completed") != 2 || messages.Count(item => item.State == "interrupted") != 1) throw new InvalidDataException("Resumed execution did not preserve partial and final turns.");
                    }
                    finally { await engine.DisposeAsync().ConfigureAwait(false); }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task<DebateRun> WaitStateAsync(SqliteDatabaseDriver database, RequestContext context, string conversation, string runId, string expected, CancellationToken token)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                DebateRun run = await database.Runs.ReadAsync(context, conversation, runId, token).ConfigureAwait(false);
                if (run.State == expected) return run;
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            throw new TimeoutException("Run did not reach " + expected + ".");
        }
        private static Task ExclusiveHomeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string home = Path.Combine(Path.GetTempPath(), "arguebot-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                using (FileStream first = paths.AcquireLock())
                {
                    bool rejected = false;
                    try { using (FileStream second = paths.AcquireLock()) { } }
                    catch (IOException) { rejected = true; }
                    if (!rejected) throw new InvalidDataException("Second owner acquired home.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
            return Task.CompletedTask;
        }
    }
}
