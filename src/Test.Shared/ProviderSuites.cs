namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Backups;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using Npgsql;
    using Touchstone.Core;

    /// <summary>Real database provider contract tests against explicitly configured disposable databases.</summary>
    public static class ProviderSuites
    {
        /// <summary>Provider-matrix descriptors. Missing service credentials are reported as skips.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                foreach (DatabaseTypeEnum provider in new[] { DatabaseTypeEnum.Postgresql })
                {
                    string variable = "ARGUEBOT_TEST_" + provider.ToString().ToUpperInvariant();
                    bool missing = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
                    cases.Add(new TestCaseDescriptor("Providers", provider.ToString(), provider + " relational lifecycle and ownership", token => RunAsync(provider, variable, token), skip: missing, skipReason: missing ? "Set " + variable + " for a disposable test database." : null));
                    cases.Add(new TestCaseDescriptor("Providers", "PostgresqlOwnership", "PostgreSQL rejects a second writer and never reconnects a lost owner", cancellation => OwnershipAsync(variable, cancellation), skip: missing, skipReason: missing ? "Set " + variable + " for a disposable test database." : null));
                    cases.Add(new TestCaseDescriptor("Providers", "PostgresqlRecovery", "Complete archives restore SQLite history into an empty PostgreSQL database and reject existing data", cancellation => PostgresqlRecoveryAsync(variable, cancellation), skip: missing, skipReason: missing ? "Set " + variable + " for a disposable test database." : null));
                }
                return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Providers", "Database provider contracts", cases) };
            }
        }
        private static async Task PostgresqlRecoveryAsync(string variable, CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-pg-recovery-" + Guid.NewGuid().ToString("N"));
            string name = "arguebot_restore_" + Guid.NewGuid().ToString("N");
            string reference = "ARGUEBOT_RECOVERY_TARGET_" + Guid.NewGuid().ToString("N");
            bool created = false;
            try
            {
                NpgsqlConnectionStringBuilder connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(variable)) { Pooling = false };
                using (NpgsqlConnection control = new NpgsqlConnection(connection.ConnectionString))
                {
                    await control.OpenAsync(token).ConfigureAwait(false);
                    using (NpgsqlCommand create = new NpgsqlCommand("CREATE DATABASE \"" + name + "\"", control)) { await create.ExecuteNonQueryAsync(token).ConfigureAwait(false); created = true; }
                }
                connection.Database = name; Environment.SetEnvironmentVariable(reference, connection.ConnectionString);
                ArgueBotPaths sourcePaths = new ArgueBotPaths(Path.Combine(home, "source"));
                await new ServiceSettings().SaveAsync(sourcePaths, token).ConfigureAwait(false);
                RequestContext owner = new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true };
                Conversation conversation;
                string archive = Path.Combine(home, "source.arguebak");
                using (SqliteDatabaseDriver source = new SqliteDatabaseDriver(sourcePaths))
                {
                    await source.InitializeAsync(token).ConfigureAwait(false);
                    conversation = await source.CreateConversationAsync(owner, "Portable recovery into PostgreSQL 議論", token).ConfigureAwait(false);
                    await source.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = "Keep exact history across both supported providers." }, token: token).ConfigureAwait(false);
                    await RecoveryArchive.CreateAsync(source, sourcePaths, archive, token: token).ConfigureAwait(false);
                }
                DatabaseSettings target = new DatabaseSettings { Type = DatabaseTypeEnum.Postgresql, ConnectionEnvironmentVariable = reference };
                using (VerifiedRecovery recovery = await RecoveryArchive.OpenAsync(archive, token: token).ConfigureAwait(false))
                {
                    string destination = Path.Combine(home, "recovered");
                    await RecoveryRestorer.ToNewPostgresqlHomeAsync(recovery, destination, target, 4549, token: token).ConfigureAwait(false);
                    ServiceSettings settings = await ServiceSettings.LoadAsync(new ArgueBotPaths(destination), token).ConfigureAwait(false);
                    if (settings.Database.Type != DatabaseTypeEnum.Postgresql || settings.Database.ConnectionEnvironmentVariable != reference || settings.Database.ConnectionString.Length != 0 || File.Exists(Path.Combine(destination, "runtime", "recovery-incomplete"))) throw new InvalidDataException("PostgreSQL recovery published incorrect credentials or an incomplete home.");
                    using (DatabaseDriverBase restored = DatabaseDriverFactory.Create(target))
                    {
                        await restored.InitializeAsync(token).ConfigureAwait(false);
                        if ((await restored.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Single().Content != "Keep exact history across both supported providers.") throw new InvalidDataException("PostgreSQL recovery lost exact source history.");
                    }
                    bool refused = false;
                    try { await RecoveryRestorer.ToNewPostgresqlHomeAsync(recovery, Path.Combine(home, "must-not-publish"), target, token: token).ConfigureAwait(false); }
                    catch (DomainException exception) when (exception.Code == "restore_target_not_empty") { refused = true; }
                    if (!refused || Directory.Exists(Path.Combine(home, "must-not-publish"))) throw new InvalidDataException("Recovery reused a nonempty PostgreSQL destination.");
                    using (DatabaseDriverBase preserved = DatabaseDriverFactory.Create(target))
                    {
                        await preserved.InitializeAsync(token).ConfigureAwait(false);
                        if ((await preserved.ReadConversationAsync(owner, conversation.Id, token).ConfigureAwait(false)).Title != conversation.Title) throw new InvalidDataException("Refusing a nonempty target changed its data.");
                    }
                    NpgsqlConnectionStringBuilder reset = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(variable)) { Pooling = false };
                    using (NpgsqlConnection control = new NpgsqlConnection(reset.ConnectionString))
                    {
                        await control.OpenAsync(token).ConfigureAwait(false);
                        using (NpgsqlCommand command = control.CreateCommand())
                        {
                            command.CommandText = "DROP DATABASE \"" + name + "\" WITH (FORCE)"; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                            command.CommandText = "CREATE DATABASE \"" + name + "\""; await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        }
                    }
                    RecoveryPublicationException? failure = null;
                    try { await RecoveryRestorer.ToNewPostgresqlHomeAsync(recovery, Path.Combine(home, "publication-refused"), target, 4549, new FailingRecoveryPublisher(), token).ConfigureAwait(false); }
                    catch (RecoveryPublicationException exception) { failure = exception; }
                    if (failure == null || !Directory.Exists(failure.StagingHome) || !File.Exists(Path.Combine(failure.StagingHome, "backups", "restored-database.arguedb.jsonl")) || Directory.Exists(Path.Combine(home, "publication-refused"))) throw new InvalidDataException("A committed PostgreSQL restore lost its protected staging after publication failure.");
                    using (DatabaseDriverBase preserved = DatabaseDriverFactory.Create(target))
                    {
                        await preserved.InitializeAsync(token).ConfigureAwait(false);
                        if ((await preserved.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Single().Content != "Keep exact history across both supported providers.") throw new InvalidDataException("Publication failure lost committed destination history.");
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(reference, null);
                if (created)
                {
                    using (NpgsqlConnection control = new NpgsqlConnection(Environment.GetEnvironmentVariable(variable)))
                    {
                        await control.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                        using (NpgsqlCommand drop = new NpgsqlCommand("DROP DATABASE IF EXISTS \"" + name + "\" WITH (FORCE)", control)) { await drop.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false); }
                    }
                }
                if (Directory.Exists(home)) Directory.Delete(home, true);
            }
        }
        private static async Task OwnershipAsync(string variable, CancellationToken token)
        {
            DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Postgresql, ConnectionEnvironmentVariable = variable };
            using (DatabaseDriverBase first = DatabaseDriverFactory.Create(settings))
            {
                await first.InitializeAsync(token).ConfigureAwait(false);
                using (DatabaseDriverBase competitor = DatabaseDriverFactory.Create(settings))
                {
                    bool refused = false;
                    try { await competitor.InitializeAsync(token).ConfigureAwait(false); }
                    catch (IOException) { refused = true; }
                    if (!refused) throw new InvalidDataException("Two service owners acquired the same PostgreSQL database.");
                }
                using (NpgsqlConnection control = new NpgsqlConnection(Environment.GetEnvironmentVariable(variable)))
                {
                    await control.OpenAsync(token).ConfigureAwait(false);
                    using (NpgsqlCommand terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname=current_database() AND application_name='ArgueBot exclusive writer'", control))
                    { await terminate.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                }
                RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true };
                bool lost = false;
                try { await first.CreateConversationAsync(context, "Must not commit after writer loss", token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is NpgsqlException || exception is OperationCanceledException || exception is InvalidOperationException) { lost = true; }
                if (!lost || !first.OwnershipToken.IsCancellationRequested) throw new InvalidDataException("Writer loss did not fail closed and cancel the service ownership signal.");
                using (DatabaseDriverBase successor = DatabaseDriverFactory.Create(settings))
                {
                    await successor.InitializeAsync(token).ConfigureAwait(false);
                    lost = false;
                    try { await first.CreateConversationAsync(context, "Old owner must remain fenced", token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { lost = true; }
                    if (!lost) throw new InvalidDataException("The lost owner transparently reconnected alongside its successor.");
                    Conversation conversation = await successor.CreateConversationAsync(context, "Successor owns recovered storage", token).ConfigureAwait(false);
                    await successor.DeleteConversationAsync(context, conversation.Id, token).ConfigureAwait(false);
                }
            }
        }
        private static async Task RunAsync(DatabaseTypeEnum provider, string variable, CancellationToken token)
        {
            DatabaseSettings settings = new DatabaseSettings { Type = provider, ConnectionEnvironmentVariable = variable };
            string migrationHome = Path.Combine(Path.GetTempPath(), "arguebot-provider-upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(migrationHome); HomePermissions.Directory(migrationHome);
            try
            {
                using (DatabaseDriverBase database = DatabaseDriverFactory.Create(settings, migrationHome))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    using (NpgsqlConnection authority = new NpgsqlConnection(Environment.GetEnvironmentVariable(variable)))
                    { await authority.OpenAsync(token).ConfigureAwait(false); await StoredAuthorizationChecks.RunAsync(database, authority, token).ConfigureAwait(false); await RoleChecks.RunAsync(database, authority, token).ConfigureAwait(false); }
                    await DefaultAccessChecks.RunAsync(database, token).ConfigureAwait(false);
                    await AuditChecks.RunAsync(database, token).ConfigureAwait(false);
                    await IdentityChecks.RunAsync(database, new ArgueBotPaths(migrationHome), token).ConfigureAwait(false);
                    await ExecutionIdentityChecks.RunAsync(database, new ArgueBotPaths(migrationHome), token).ConfigureAwait(false);
                    await AttemptStorageChecks.RunAsync(database, new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true }, token).ConfigureAwait(false);
                    await MessagePageChecks.RunAsync(database, new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true }, token).ConfigureAwait(false);
                    await ReplayChecks.RunAsync(database, new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true }, token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true };
                    BackupEntry archiveRecord = await database.Backups.CreateAsync(context, "Provider recovery 議論", true, false, token).ConfigureAwait(false);
                    try
                    {
                        BackupOperation operation = await database.Backups.CreateOperationAsync(context, archiveRecord.Id, "verify", token: token).ConfigureAwait(false);
                        operation.State = "completed"; operation.MissingReferences.Add("ARGUEBOT_FIXTURE_MISSING");
                        await database.Backups.SaveOperationAsync(context, operation, token).ConfigureAwait(false);
                        operation = await database.Backups.ReadOperationAsync(context, operation.Id, token).ConfigureAwait(false);
                        BackupOperationPage jobs = await database.Backups.EnumerateOperationsAsync(context, archiveRecord.Id, token: token).ConfigureAwait(false);
                        if (jobs.TotalCount != 1 || jobs.Items.Single().Id != operation.Id) throw new InvalidDataException("Provider operation paging lost a durable job.");
                        await database.Backups.SetStatusAsync(context, archiveRecord.Id, "ready", 2147483650L, new string('a', 64), token).ConfigureAwait(false);
                        BackupPage records = await database.Backups.EnumerateAsync(context, "Provider recovery 議論", token: token).ConfigureAwait(false);
                        if (records.TotalCount != 1 || records.Items.Single().Bytes != 2147483650L || operation.MissingReferences.Single() != "ARGUEBOT_FIXTURE_MISSING") throw new InvalidDataException("Provider corrupted recovery metadata, large sizes or operation references.");
                    }
                    finally { await database.Backups.DeleteAsync(context, archiveRecord.Id, CancellationToken.None).ConfigureAwait(false); }
                    Conversation conversation = await database.CreateConversationAsync(context, "Provider contract 議論 ' % _ " + Guid.NewGuid().ToString("N"), token).ConfigureAwait(false);
                    try
                    {
                        Message message = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "provider-needle 議論" }, "unique", token).ConfigureAwait(false);
                        Message duplicate = await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "provider-needle 議論" }, "unique", token).ConfigureAwait(false);
                        if (message.Id != duplicate.Id) throw new InvalidDataException("Receipt did not deduplicate.");
                        List<Message> messages = await database.ReadMessagesAsync(context, conversation.Id, token).ConfigureAwait(false);
                        if (messages.Single().Content != message.Content) throw new InvalidDataException("Provider corrupted message content.");
                        Page page = await database.EnumerateConversationsAsync(context, conversation.Title, token: token).ConfigureAwait(false);
                        if (page.TotalCount != 1) throw new InvalidDataException("Provider search/paging failed.");
                        Conversation updated = await database.ReadConversationAsync(context, conversation.Id, token).ConfigureAwait(false);
                        updated = await database.UpdateConversationAsync(context, conversation.Id, new ConversationRequest { Title = updated.Title, Pinned = true, Revision = updated.Revision }, token).ConfigureAwait(false);
                        if (!updated.Pinned) throw new InvalidDataException("Provider update failed.");
                        RequestContext foreign = new RequestContext { TenantId = "ten_foreign", UserId = "usr_local", IsAuthenticated = true, Role = "admin" };
                        bool denied = false;
                        try { await database.ReadConversationAsync(foreign, conversation.Id, token).ConfigureAwait(false); }
                        catch (DomainException ex) when (ex.StatusCode == 404) { denied = true; }
                        if (!denied) throw new InvalidDataException("Provider leaked foreign tenant history.");
                        string snapshotHome = Path.Combine(Path.GetTempPath(), "arguebot-provider-snapshot-" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            ArgueBotPaths targetPaths = new ArgueBotPaths(snapshotHome); targetPaths.EnsureDirectories();
                            string snapshot = Path.Combine(snapshotHome, "provider.arguedb.jsonl");
                            await database.ExportSnapshotAsync(snapshot, token).ConfigureAwait(false);
                            using (FileStream owner = targetPaths.AcquireLock())
                            using (SqliteDatabaseDriver restored = new SqliteDatabaseDriver(targetPaths))
                            {
                                await restored.InitializeAsync(token).ConfigureAwait(false);
                                await restored.RestoreSnapshotAsync(snapshot, token).ConfigureAwait(false);
                                if ((await restored.ReadMessagesAsync(context, conversation.Id, token).ConfigureAwait(false)).Single().Content != message.Content) throw new InvalidDataException("Provider snapshot did not restore into SQLite.");
                            }
                        }
                        finally { if (Directory.Exists(snapshotHome)) Directory.Delete(snapshotHome, true); }
                        List<DurableEvent> events = await database.ReadEventsAsync(context, conversation.Id, token: token).ConfigureAwait(false);
                        if (events.Count != 3 || events[2].Sequence != 3) throw new InvalidDataException("Provider event ordering failed.");
                    }
                    finally { await database.DeleteConversationAsync(context, conversation.Id, CancellationToken.None).ConfigureAwait(false); }
                }
            }
            finally { Directory.Delete(migrationHome, true); }
        }
    }
}
