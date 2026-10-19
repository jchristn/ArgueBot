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
    using ArgueBot.Core.Security;
    using Touchstone.Core;

    /// <summary>Logical snapshot integrity and transaction rollback using separate disposable homes.</summary>
    public static class BackupSuites
    {
        /// <summary>Snapshot descriptors shared by all test adapters.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Backup", "Portable snapshots", new List<TestCaseDescriptor> { new TestCaseDescriptor("Backup", "Inventory", "Global recovery inventory denies tenant administrators and never replays interrupted jobs", InventoryAsync), new TestCaseDescriptor("Backup", "Archive", "Recovery archives exclude secrets by default, protect opt-in keys and reject traversal", ArchiveAsync), new TestCaseDescriptor("Backup", "Encryption", "Recovery encryption rejects wrong keys, damaged/reordered frames and missing terminal authentication", EncryptionAsync), new TestCaseDescriptor("Backup", "Restore", "Portable snapshot, checksum refusal and failed-restore rollback preserve data", RestoreAsync) }) }; }
        }
        private static async Task InventoryAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-inventory-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext owner = Owner(); owner.IsAdmin = true;
                    BackupEntry entry = await database.Backups.CreateAsync(owner, "Inventário 議論", false, false, token).ConfigureAwait(false);
                    foreach (RequestContext deniedContext in new[] { new RequestContext(), new RequestContext { TenantId = owner.TenantId, UserId = owner.UserId, IsAuthenticated = true, IsTenantAdmin = true, Role = "admin" }, new RequestContext { TenantId = "ten_foreign", UserId = "usr_foreign", IsAuthenticated = true, IsTenantAdmin = true, Role = "admin" } })
                    {
                        bool denied = false;
                        try { await database.Backups.ReadAsync(deniedContext, entry.Id, token).ConfigureAwait(false); }
                        catch (ArgueBot.Core.Services.DomainException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403) { denied = true; }
                        if (!denied) throw new InvalidDataException("A tenant principal accessed global recovery metadata.");
                    }
                    BackupOperation operation = await database.Backups.CreateOperationAsync(owner, entry.Id, "create", token: token).ConfigureAwait(false);
                    bool busy = false;
                    try { await database.Backups.EnsureIdleAsync(owner, entry.Id, token).ConfigureAwait(false); }
                    catch (ArgueBot.Core.Services.DomainException ex) when (ex.StatusCode == 409) { busy = true; }
                    if (!busy) throw new InvalidDataException("An in-flight archive was eligible for deletion.");
                    await database.Backups.ReconcileAsync(token).ConfigureAwait(false);
                    operation = await database.Backups.ReadOperationAsync(owner, operation.Id, token).ConfigureAwait(false);
                    BackupOperationPage jobs = await database.Backups.EnumerateOperationsAsync(owner, entry.Id, size: 1, token: token).ConfigureAwait(false);
                    if (jobs.TotalCount != 1 || jobs.Items.Single().Id != operation.Id || jobs.PageSize != 1) throw new InvalidDataException("Recovery operation paging lost durable jobs or exact totals.");
                    if (operation.State != "failed" || operation.ErrorCode != "service_interrupted" || (await database.Backups.ReadAsync(owner, entry.Id, token).ConfigureAwait(false)).Status != "unavailable") throw new InvalidDataException("Interrupted recovery was replayed or advertised as ready.");
                    BackupPage page = await database.Backups.EnumerateAsync(owner, "議論", token: token).ConfigureAwait(false);
                    if (page.TotalCount != 1) throw new InvalidDataException("Recovery metadata search lost Unicode or exact totals.");
                    await database.Backups.DeleteAsync(owner, entry.Id, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task ArchiveAsync(CancellationToken token)
        {
            string root = Path.Combine(Path.GetTempPath(), "arguebot-archive-" + Guid.NewGuid().ToString("N"));
            string escapeName = "arguebot-escape-" + Guid.NewGuid().ToString("N");
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(root);
                using (FileStream owner = paths.AcquireLock())
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    ServiceSettings settings = await ServiceSettings.LoadAsync(paths, token).ConfigureAwait(false);
                    settings.Database.ConnectionString = "fixture-secret-connection-not-for-production"; await settings.SaveAsync(paths, token).ConfigureAwait(false);
                    TokenProtector protector = new TokenProtector(paths); protector.Rotate();
                    CertificateFixture.Create(paths);
                    SessionMethods sessions = new SessionMethods(database, protector);
                    string originalSession = await sessions.IssueLocalAsync(token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(Owner(), "Recover the complete thread", token).ConfigureAwait(false);
                    await database.AppendMessageAsync(Owner(), new Message { ConversationId = conversation.Id, Content = "The recovered history remains exact." }, token: token).ConfigureAwait(false);
                    string reference = "ARGUEBOT_RECOVERY_FIXTURE_" + Guid.NewGuid().ToString("N");
                    await database.Profiles.CreateAsync(Owner(), new RunnerProfile { Name = "recovery fixture", Kind = "rest", Provider = "ollama", Endpoint = "http://127.0.0.1:11434", Model = "fixture", SecretEnvironmentVariable = reference }, token).ConfigureAwait(false);
                    string archive = Path.Combine(root, "default.arguebak");
                    await RecoveryArchive.CreateAsync(database, paths, archive, token: token).ConfigureAwait(false);
                    using (VerifiedRecovery recovered = await RecoveryArchive.OpenAsync(archive, token: token).ConfigureAwait(false))
                    {
                        if (recovered.Manifest.IncludesSecrets || recovered.Encrypted || recovered.Settings.Database.ConnectionString.Length != 0 || recovered.Manifest.Files.Any(file => file.Path.StartsWith("keys/", StringComparison.Ordinal)) || !recovered.MissingReferences.Contains(reference)) throw new InvalidDataException("Default recovery copied secrets or lost credential reference diagnostics.");
                    }
                    string encrypted = Path.Combine(root, "encrypted.arguebak");
                    await RecoveryArchive.CreateAsync(database, paths, encrypted, true, "fixture-recovery-passphrase", token: token).ConfigureAwait(false);
                    using (VerifiedRecovery recovered = await RecoveryArchive.OpenAsync(encrypted, "fixture-recovery-passphrase", token: token).ConfigureAwait(false))
                    {
                        if (!recovered.Manifest.IncludesSecrets || !recovered.Encrypted || recovered.Settings.Database.ConnectionString != settings.Database.ConnectionString || !File.Exists(Path.Combine(recovered.DirectoryPath, "keys", "session-current")) || !File.Exists(Path.Combine(recovered.DirectoryPath, "keys", "listener.pfx"))) throw new InvalidDataException("Encrypted opt-in recovery did not preserve protected configuration and keys.");
                        TokenProtector restored = new TokenProtector(new ArgueBotPaths(recovered.DirectoryPath));
                        string session = protector.Protect(new SessionClaims { SessionId = "ses_fixture", TenantId = "ten_local", UserId = "usr_local", IssuedUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddMinutes(5) });
                        if (restored.Unprotect(session) == null) throw new InvalidDataException("Recovered key ring could not read source ciphertext.");
                        string destination = Path.Combine(root, "restored-home");
                        RecoveryRestoreResult result = await RecoveryRestorer.ToNewHomeAsync(recovered, destination, 4543, token).ConfigureAwait(false);
                        using (SqliteDatabaseDriver restoredDatabase = new SqliteDatabaseDriver(new ArgueBotPaths(destination)))
                        {
                            await restoredDatabase.InitializeAsync(token).ConfigureAwait(false);
                            if ((await restoredDatabase.ReadMessagesAsync(Owner(), conversation.Id, token).ConfigureAwait(false)).Single().Content != "The recovered history remains exact.") throw new InvalidDataException("Complete-home recovery lost conversation content.");
                            SessionMethods restoredSessions = new SessionMethods(restoredDatabase, new TokenProtector(new ArgueBotPaths(destination)));
                            if (await restoredSessions.AuthenticateAsync(originalSession, token).ConfigureAwait(false) != null || !result.SessionsRevoked) throw new InvalidDataException("A restored session remained authorized.");
                        }
                        bool existingDenied = false;
                        try { await RecoveryRestorer.ToNewHomeAsync(recovered, destination, token: token).ConfigureAwait(false); }
                        catch (IOException) { existingDenied = true; }
                        if (!existingDenied) throw new InvalidDataException("Recovery overwrote an existing home.");
                    }
                    string unsafeArchive = Path.Combine(root, "unsafe.arguebak"); File.Copy(archive, unsafeArchive);
                    using (ZipArchive zip = ZipFile.Open(unsafeArchive, ZipArchiveMode.Update))
                    using (StreamWriter writer = new StreamWriter(zip.CreateEntry("../" + escapeName).Open())) { await writer.WriteAsync("must not escape").ConfigureAwait(false); }
                    bool denied = false;
                    try { using (VerifiedRecovery recovered = await RecoveryArchive.OpenAsync(unsafeArchive, token: token).ConfigureAwait(false)) { } }
                    catch (InvalidDataException) { denied = true; }
                    if (!denied || File.Exists(Path.Combine(Path.GetTempPath(), escapeName))) throw new InvalidDataException("Recovery accepted a traversal entry.");
                    denied = false;
                    try { using (VerifiedRecovery recovered = await RecoveryArchive.OpenAsync(archive, limits: new RecoveryLimits { MaximumBytes = 10 }, token: token).ConfigureAwait(false)) { } }
                    catch (InvalidDataException) { denied = true; }
                    if (!denied) throw new InvalidDataException("Recovery ignored its byte quota.");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static async Task EncryptionAsync(CancellationToken token)
        {
            byte[] original = new byte[131209]; new Random(771).NextBytes(original);
            byte[] encrypted;
            using (MemoryStream input = new MemoryStream(original))
            using (MemoryStream output = new MemoryStream())
            { await BundleEncryption.EncryptAsync(input, output, "fixture-recovery-passphrase", token).ConfigureAwait(false); encrypted = output.ToArray(); }
            using (MemoryStream input = new MemoryStream(encrypted))
            using (MemoryStream output = new MemoryStream())
            { await BundleEncryption.DecryptAsync(input, output, "fixture-recovery-passphrase", token: token).ConfigureAwait(false); if (!output.ToArray().SequenceEqual(original)) throw new InvalidDataException("Encrypted recovery changed plaintext."); }
            using (MemoryStream input = new MemoryStream(original))
            using (MemoryStream output = new MemoryStream())
            { await BundleEncryption.EncryptAsync(input, output, "fixture-recovery-passphrase", token).ConfigureAwait(false); if (output.ToArray().SequenceEqual(encrypted)) throw new InvalidDataException("Recovery encryption reused its salt/nonce."); }
            byte[] damaged = (byte[])encrypted.Clone(); damaged[80] ^= 1;
            byte[] reordered = (byte[])encrypted.Clone();
            const int frame = 65536 + 20;
            encrypted.AsSpan(44, frame).CopyTo(reordered.AsSpan(44 + frame, frame));
            encrypted.AsSpan(44 + frame, frame).CopyTo(reordered.AsSpan(44, frame));
            foreach (byte[] invalid in new byte[][] { damaged, reordered, encrypted.Take(encrypted.Length - 20).ToArray() })
            {
                bool denied = false;
                using (MemoryStream input = new MemoryStream(invalid))
                using (MemoryStream output = new MemoryStream())
                {
                    try { await BundleEncryption.DecryptAsync(input, output, "fixture-recovery-passphrase", token: token).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is CryptographicException || ex is EndOfStreamException || ex is InvalidDataException) { denied = true; }
                }
                if (!denied) throw new InvalidDataException("Damaged or incomplete encrypted recovery was accepted.");
            }
            bool wrongKeyDenied = false;
            using (MemoryStream input = new MemoryStream(encrypted))
            using (MemoryStream output = new MemoryStream())
            {
                try { await BundleEncryption.DecryptAsync(input, output, "different-fixture-passphrase", token: token).ConfigureAwait(false); }
                catch (CryptographicException) { wrongKeyDenied = true; }
            }
            if (!wrongKeyDenied) throw new InvalidDataException("A wrong recovery password was accepted.");
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
                    SessionMethods restoredSessions = new SessionMethods(target, new TokenProtector(targetPaths));
                    string restoredSession = await restoredSessions.IssueLocalAsync(token).ConfigureAwait(false);
                    if (!(await restoredSessions.AuthenticateAsync(restoredSession, token).ConfigureAwait(false))!.IsAdmin) throw new InvalidDataException("Historical restore failed to migrate explicit owner privileges.");
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
                    if (record.Table == "generation_attempts" || record.Table == "attempt_context") continue;
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
