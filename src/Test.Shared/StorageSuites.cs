namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
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
                    new TestCaseDescriptor("Storage", "RequestHistory", "Request metadata paging, private detail, isolation, empty buckets and retention", RequestHistoryAsync),
                    new TestCaseDescriptor("Storage", "RoundTrip", "History, receipts, search, backup and tenant isolation", RoundTripAsync),
                    new TestCaseDescriptor("Storage", "RunRecovery", "Immutable snapshots and interrupted run recovery", RunRecoveryAsync),
                    new TestCaseDescriptor("Storage", "ExclusiveHome", "Exclusive application-home ownership", ExclusiveHomeAsync),
                    new TestCaseDescriptor("Storage", "CommandReceipts", "Moderator retries do not extend twice; immediate pause/resume retires the old worker", CommandReceiptsAsync),
                    new TestCaseDescriptor("Storage", "Engine", "Service-owned execution, interruption and resumption", EngineAsync),
                    new TestCaseDescriptor("Storage", "SchemaUpgrade", "Historical schema upgrade creates backup and rejects future schemas", SchemaUpgradeAsync)
                }) };
            }
        }
        private static RequestContext Owner() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" }; }
        private static async Task RequestHistoryAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-history-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    RequestHistoryEntry entry = new RequestHistoryEntry { TenantId = "ten_local", UserId = "usr_local", Method = "GET", Path = "/fixture", Status = 200, RawResponseBody = "redacted diagnostic", CreatedUtc = start.AddMinutes(5), Headers = new List<CapturedHeader> { new CapturedHeader { Name = "Authorization", Value = "[redacted]" } } };
                    await database.History.InsertAsync(entry, token).ConfigureAwait(false);
                    await database.History.InsertAsync(new RequestHistoryEntry { TenantId = "ten_foreign", UserId = "usr_foreign", Method = "GET", Path = "/foreign", Status = 500, CreatedUtc = start }, token).ConfigureAwait(false);
                    HistoryPage page = await database.History.EnumerateAsync(Owner(), new HistoryFilter { Search = "fixture" }, token).ConfigureAwait(false);
                    if (page.TotalCount != 1 || page.Items.Count != 1 || page.Items[0].RawResponseBody.Length != 0 || page.Items[0].Headers.Count != 0) throw new InvalidDataException("History list exposed detail or returned incorrect totals.");
                    RequestHistoryEntry detail = await database.History.ReadAsync(Owner(), entry.Id, token).ConfigureAwait(false);
                    if (detail.RawResponseBody != entry.RawResponseBody || detail.Headers.Count != 1 || detail.Headers[0].Value != "[redacted]") throw new InvalidDataException("Diagnostic detail did not round trip.");
                    HistoryPage all = await database.History.EnumerateAsync(Owner(), new HistoryFilter(), token).ConfigureAwait(false);
                    if (all.TotalCount != 1) throw new InvalidDataException("History crossed tenant boundary.");
                    List<ActivityBucket> buckets = await database.History.SummaryAsync(Owner(), start, start.AddHours(3), 60, token).ConfigureAwait(false);
                    if (buckets.Count != 3 || buckets[0].Success != 1 || buckets[0].Failure != 0 || buckets[1].Success != 0 || buckets[2].Failure != 0) throw new InvalidDataException("Summary omitted empty buckets or crossed tenant boundary.");
                    RequestContext viewer = Owner(); viewer.Role = "viewer";
                    bool denied = false;
                    try { await database.History.ReadAsync(viewer, entry.Id, token).ConfigureAwait(false); }
                    catch (DomainException ex) when (ex.StatusCode == 403) { denied = true; }
                    if (!denied) throw new InvalidDataException("Viewer read operational diagnostics.");
                    await database.History.PruneAsync(start.AddMinutes(10), token).ConfigureAwait(false);
                    if ((await database.History.EnumerateAsync(Owner(), new HistoryFilter(), token).ConfigureAwait(false)).TotalCount != 0) throw new InvalidDataException("Retention did not remove expired captures.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
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
                    await database.Profiles.DeleteAsync(context, one.Id, token).ConfigureAwait(false);
                    repeated = await database.Runs.CreateAsync(context, conversationId, request, token).ConfigureAwait(false);
                    if (repeated.Id != run.Id) throw new InvalidDataException("Deleting the reusable profile broke a committed run retry.");
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
        private static async Task SchemaUpgradeAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); paths.EnsureDirectories();
                using (SqliteConnection connection = new SqliteConnection("Data Source=" + paths.DatabaseFile + ";Pooling=False"))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "sqlite-schema-v1.sql"), token).ConfigureAwait(false);
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    if (Directory.GetFiles(Path.Combine(home, "backups"), "before-schema-1-*.db").Length != 1) throw new InvalidDataException("Upgrade did not make a backup.");
                    Conversation conversation = await database.CreateConversationAsync(Owner(), "migrated", token).ConfigureAwait(false);
                    await database.AppendMessageAsync(Owner(), new Message { ConversationId = conversation.Id, Content = "new schema message" }, token: token).ConfigureAwait(false);
                }
                using (SqliteConnection connection = new SqliteConnection("Data Source=" + paths.DatabaseFile + ";Pooling=False"))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand()) { command.CommandText = "INSERT INTO schema_migrations(version,applied_utc) VALUES(999,'future')"; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                }
                bool rejected = false;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    try { await database.InitializeAsync(token).ConfigureAwait(false); }
                    catch (InvalidDataException) { rejected = true; }
                }
                if (!rejected) throw new InvalidDataException("Future schema was accepted.");
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task CommandReceiptsAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-command-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext owner = Owner();
                    RunnerProfile first = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "first", Executable = "fixture" }, token).ConfigureAwait(false);
                    RunnerProfile second = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "second", Executable = "fixture" }, token).ConfigureAwait(false);
                    FixtureRunner runner = new FixtureRunner { Wait = true };
                    ConversationEngine engine = new ConversationEngine(database, runner, 1);
                    try
                    {
                        Conversation blocker = await database.CreateConversationAsync(owner, "occupy worker capacity", token).ConfigureAwait(false);
                        DebateRun blocking = await engine.StartAsync(owner, blocker.Id, new CreateRunRequest { ParticipantIds = new List<string> { first.Id, second.Id }, MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "blocker" }, token).ConfigureAwait(false);
                        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                        Conversation target = await database.CreateConversationAsync(owner, "queued command fixture", token).ConfigureAwait(false);
                        DebateRun run = await engine.StartAsync(owner, target.Id, new CreateRunRequest { ParticipantIds = new List<string> { first.Id, second.Id }, MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "target" }, token).ConfigureAwait(false);
                        CommandRequest extension = new CommandRequest { Action = "extend", Rounds = 2, Revision = run.Revision, IdempotencyKey = "extend-once" };
                        await engine.ApplyAsync(owner, target.Id, run.Id, extension, "fixture", token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, target.Id, run.Id, extension, "fixture", token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(owner, target.Id, run.Id, token).ConfigureAwait(false);
                        if (run.MaxRounds != 3) throw new InvalidDataException("A duplicate extension changed the round limit twice.");
                        CommandRequest note = new CommandRequest { Action = "note", Text = "Keep the trade-offs explicit.", Revision = run.Revision, IdempotencyKey = "note-once" };
                        CommandRequest directive = new CommandRequest { Action = "steer", Text = "Consider the failure case.", RecipientId = run.Participants[0].Id, Revision = run.Revision, IdempotencyKey = "steer-once" };
                        await engine.ApplyAsync(owner, target.Id, run.Id, note, "fixture", token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, target.Id, run.Id, directive, "fixture", token).ConfigureAwait(false);
                        bool denied = false;
                        try { await engine.ApplyAsync(owner, target.Id, run.Id, new CommandRequest { Action = "extend", Rounds = 3, IdempotencyKey = extension.IdempotencyKey }, "fixture", token).ConfigureAwait(false); }
                        catch (DomainException ex) when (ex.Code == "idempotency_conflict") { denied = true; }
                        if (!denied) throw new InvalidDataException("A reused moderator key accepted changed input.");
                        await engine.ApplyAsync(owner, target.Id, run.Id, new CommandRequest { Action = "pause", IdempotencyKey = "pause" }, "fixture", token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, target.Id, run.Id, new CommandRequest { Action = "resume", IdempotencyKey = "resume" }, "fixture", token).ConfigureAwait(false);
                        runner.Wait = false;
                        await engine.ApplyAsync(owner, blocker.Id, blocking.Id, new CommandRequest { Action = "stop", IdempotencyKey = "stop-blocker" }, "fixture", token).ConfigureAwait(false);
                        run = await WaitStateAsync(database, owner, target.Id, run.Id, "completed", token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, target.Id, run.Id, note, "fixture", token).ConfigureAwait(false);
                        await engine.ApplyAsync(owner, target.Id, run.Id, directive, "fixture", token).ConfigureAwait(false);
                        List<Message> retained = await database.ReadMessagesAsync(owner, target.Id, token).ConfigureAwait(false);
                        if (retained.Count(message => message.Kind == "note") != 1 || retained.Count(message => message.Kind == "steer") != 1) throw new InvalidDataException("A committed note/directive retry duplicated after completion.");
                        if (runner.Calls != 7 || run.MaxRounds != 3) throw new InvalidDataException("Immediate resumption lost or duplicated a worker.");
                    }
                    finally { await engine.DisposeAsync().ConfigureAwait(false); }
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
                        await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "interrupt", IdempotencyKey = "interrupt-fixture" }, "test-client", token).ConfigureAwait(false);
                        run = await WaitStateAsync(database, context, conversation.Id, run.Id, "paused", token).ConfigureAwait(false);
                        if (runner.Calls != 1) throw new InvalidDataException("Interrupted generation was duplicated.");
                        runner.Wait = false;
                        await engine.ApplyAsync(context, conversation.Id, run.Id, new CommandRequest { Action = "resume", Revision = run.Revision, IdempotencyKey = "resume-fixture" }, "test-client", token).ConfigureAwait(false);
                        run = await WaitStateAsync(database, context, conversation.Id, run.Id, "completed", token).ConfigureAwait(false);
                        List<Message> messages = await database.ReadMessagesAsync(context, conversation.Id, token).ConfigureAwait(false);
                        if (run.Outcome != "max_rounds" || messages.Count(item => item.State == "completed") != 2 || messages.Count(item => item.State == "interrupted") != 1) throw new InvalidDataException("Resumed execution did not preserve partial and final turns.");
                        one.Name = "edited"; await database.Profiles.UpdateAsync(context, one.Id, one, token).ConfigureAwait(false);
                        DebateRun historical = await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                        if (historical.Participants[0].Name != "one") throw new InvalidDataException("Profile edit rewrote history.");
                        DebateRun followup = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Summarize the disagreement.", ParticipantIds = new List<string> { one.Id }, IdempotencyKey = "followup" }, token).ConfigureAwait(false);
                        followup = await WaitStateAsync(database, context, conversation.Id, followup.Id, "completed", token).ConfigureAwait(false);
                        messages = await database.ReadMessagesAsync(context, conversation.Id, token).ConfigureAwait(false);
                        if (followup.Mode != "followup" || messages.Count(item => item.Kind == "question") != 1 || messages.Count(item => item.Kind == "final") != 1) throw new InvalidDataException("Follow-up question and answer were not persisted.");
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
