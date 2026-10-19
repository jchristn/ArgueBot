namespace Test.Shared
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
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
                }
                return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Providers", "Database provider contracts", cases) };
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
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true };
                    BackupEntry archiveRecord = await database.Backups.CreateAsync(context, "Provider recovery 議論", true, false, token).ConfigureAwait(false);
                    try
                    {
                        BackupOperation operation = await database.Backups.CreateOperationAsync(context, archiveRecord.Id, "verify", token: token).ConfigureAwait(false);
                        operation.State = "completed"; operation.MissingReferences.Add("ARGUEBOT_FIXTURE_MISSING");
                        await database.Backups.SaveOperationAsync(context, operation, token).ConfigureAwait(false);
                        operation = await database.Backups.ReadOperationAsync(context, operation.Id, token).ConfigureAwait(false);
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
