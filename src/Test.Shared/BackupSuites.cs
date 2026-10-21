namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Backups;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using Touchstone.Core;

    /// <summary>Logical snapshot integrity and transaction rollback using separate disposable homes.</summary>
    public static class BackupSuites
    {
        /// <summary>Snapshot descriptors shared by all test adapters.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Backup", "Portable snapshots", new List<TestCaseDescriptor> { new TestCaseDescriptor("Backup", "Restore", "Portable snapshot, checksum refusal and failed-restore rollback preserve data", RestoreAsync) }) }; }
        }
        private static RequestContext Owner() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAdmin = true, IsAuthenticated = true }; }
        private static async Task RestoreAsync(CancellationToken token)
        {
            string root = Path.Combine(Path.GetTempPath(), "arguebot-backup-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths sourcePaths = new ArgueBotPaths(Path.Combine(root, "source"));
                ArgueBotPaths targetPaths = new ArgueBotPaths(Path.Combine(root, "target"));
                using (FileStream sourceOwner = sourcePaths.AcquireLock())
                using (FileStream targetOwner = targetPaths.AcquireLock())
                using (SqliteDatabaseDriver source = new SqliteDatabaseDriver(sourcePaths))
                using (SqliteDatabaseDriver target = new SqliteDatabaseDriver(targetPaths))
                {
                    await source.InitializeAsync(token).ConfigureAwait(false); await target.InitializeAsync(token).ConfigureAwait(false);
                    Conversation original = await source.CreateConversationAsync(Owner(), "portable 議論", token).ConfigureAwait(false);
                    await source.AppendMessageAsync(Owner(), new Message { ConversationId = original.Id, Content = "Exact Unicode and newlines.\nمرحبا" }, "fixture-message", token).ConfigureAwait(false);
                    Conversation sentinel = await target.CreateConversationAsync(Owner(), "keep on failed restore", token).ConfigureAwait(false);
                    string snapshot = Path.Combine(root, "snapshot.arguedb.jsonl");
                    await source.ExportSnapshotAsync(snapshot, token).ConfigureAwait(false);
                    string tampered = Path.Combine(root, "tampered.jsonl");
                    await File.WriteAllTextAsync(tampered, (await File.ReadAllTextAsync(snapshot, token).ConfigureAwait(false)).Replace("portable", "tampered", StringComparison.Ordinal), token).ConfigureAwait(false);
                    bool denied = false;
                    try { await target.RestoreSnapshotAsync(tampered, token).ConfigureAwait(false); }
                    catch (InvalidDataException) { denied = true; }
                    if (!denied || (await target.ReadConversationAsync(Owner(), sentinel.Id, token).ConfigureAwait(false)).Title != sentinel.Title) throw new InvalidDataException("Checksum rejection changed target data.");
                    string invalid = Path.Combine(root, "constraint.jsonl");
                    await InvalidConstraintSnapshotAsync(snapshot, invalid, token).ConfigureAwait(false);
                    denied = false;
                    try { await target.RestoreSnapshotAsync(invalid, token).ConfigureAwait(false); }
                    catch (SqliteException) { denied = true; }
                    if (!denied || (await target.ReadConversationAsync(Owner(), sentinel.Id, token).ConfigureAwait(false)).Title != sentinel.Title) throw new InvalidDataException("A database constraint failure did not roll back the restore.");
                    await target.RestoreSnapshotAsync(snapshot, token).ConfigureAwait(false);
                    await target.InitializeAsync(token).ConfigureAwait(false);
                    if ((await target.ReadConversationAsync(Owner(), original.Id, token).ConfigureAwait(false)).Title != original.Title || (await target.ReadMessagesAsync(Owner(), original.Id, token).ConfigureAwait(false)).Single().Content != "Exact Unicode and newlines.\nمرحبا") throw new InvalidDataException("Portable restore changed conversation content.");
                    string historical = Path.Combine(root, "schema-four.jsonl");
                    await HistoricalSnapshotAsync(snapshot, historical, token).ConfigureAwait(false);
                    await target.RestoreSnapshotAsync(historical, token).ConfigureAwait(false);
                    string intermediate = Path.Combine(root, "before-historical-upgrade.jsonl");
                    await target.ExportSnapshotAsync(intermediate, token).ConfigureAwait(false);
                    await SnapshotVerifier.ValidateAsync(intermediate, token).ConfigureAwait(false);
                    await target.InitializeAsync(token).ConfigureAwait(false);
                    if ((await target.EnumerateConversationsAsync(Owner(), token: token).ConfigureAwait(false)).TotalCount != 1) throw new InvalidDataException("Restore merged unintended target data.");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static async Task HistoricalSnapshotAsync(string source, string destination, CancellationToken token)
        {
            List<string> lines = new List<string>(); List<int> removed = new List<int>(); long rows = 0;
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (string line in await File.ReadAllLinesAsync(source, token).ConfigureAwait(false))
                {
                    BackupRecord record = JsonSerializer.Deserialize<BackupRecord>(line)!;
                    if (record.Table.StartsWith("authorization_", StringComparison.Ordinal) || record.Table == "generation_attempts" || record.Table == "attempt_context") continue;
                    if (record.Kind == "header") record.SchemaVersion = 4;
                    if (record.Kind == "table")
                    {
                        removed = record.Columns.Select((name, index) => new KeyValuePair<string, int>(name, index)).Where(column => (record.Table == "runs" && column.Key == "question") || (record.Table == "users" && new[] { "email", "password_sha256", "password_salt", "password_iterations", "revision" }.Contains(column.Key)) || column.Key == "is_admin" || column.Key == "is_tenant_admin" || column.Key == "protected" || column.Key == "text_offset" || column.Key == "target_provider" || column.Key == "target_connection_env" || column.Key == "retained_staging_home" || (record.Table == "audit_events" && new[] { "session_id", "auth_scheme", "correlation_id", "resource_type", "resource_id", "error_code" }.Contains(column.Key))).Select(column => column.Value).OrderByDescending(index => index).ToList();
                        foreach (int index in removed) record.Columns.RemoveAt(index);
                    }
                    if (record.Kind == "row") foreach (int index in removed) record.Values.RemoveAt(index);
                    if (record.Kind == "row" && record.Table == "schema_migrations" && int.Parse(record.Values[0]!, System.Globalization.CultureInfo.InvariantCulture) >= 5) continue;
                    if (record.Kind == "row") rows++;
                    if (record.Kind == "footer") { record.TotalRows = rows; record.Digest = Convert.ToHexString(hash.GetHashAndReset()); }
                    string serialized = JsonSerializer.Serialize(record); lines.Add(serialized);
                    if (record.Kind != "footer") hash.AppendData(Encoding.UTF8.GetBytes(serialized + "\n"));
                }
            }
            await File.WriteAllTextAsync(destination, string.Join("\n", lines) + "\n", token).ConfigureAwait(false);
        }
        private static async Task InvalidConstraintSnapshotAsync(string source, string destination, CancellationToken token)
        {
            List<string> lines = new List<string>(); int titleColumn = -1;
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (string line in await File.ReadAllLinesAsync(source, token).ConfigureAwait(false))
                {
                    BackupRecord record = JsonSerializer.Deserialize<BackupRecord>(line)!;
                    if (record.Kind == "table" && record.Table == "conversations") titleColumn = record.Columns.IndexOf("title");
                    if (record.Kind == "row" && record.Table == "conversations") record.Values[titleColumn] = null;
                    if (record.Kind == "footer") record.Digest = Convert.ToHexString(hash.GetHashAndReset());
                    string serialized = JsonSerializer.Serialize(record); lines.Add(serialized);
                    if (record.Kind != "footer") hash.AppendData(Encoding.UTF8.GetBytes(serialized + "\n"));
                }
            }
            await File.WriteAllTextAsync(destination, string.Join("\n", lines) + "\n", token).ConfigureAwait(false);
        }
    }
}
