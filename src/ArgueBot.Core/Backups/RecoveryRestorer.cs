namespace ArgueBot.Core.Backups
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Security;

    /// <summary>Publishes a recovered home only after complete validation and offline database restoration.</summary>
    public static class RecoveryRestorer
    {
        /// <summary>Restore into a new SQLite home without overwriting any existing destination. Source data and configuration copies remain available for rollback/reconfiguration.</summary>
        public static Task<RecoveryRestoreResult> ToNewHomeAsync(VerifiedRecovery recovery, string destination, int? port = null, CancellationToken token = default)
        { return RestoreAsync(recovery, destination, new DatabaseSettings(), port, null, token); }

        /// <summary>Restore into an explicitly provisioned empty PostgreSQL database and a new home. Existing databases with any user table are refused before schema mutation. A committed destination with failed home publication retains protected staging for operator recovery.</summary>
        public static Task<RecoveryRestoreResult> ToNewPostgresqlHomeAsync(VerifiedRecovery recovery, string destination, DatabaseSettings database, int? port = null, IRecoveryHomePublisher? publisher = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            if (database.Type != DatabaseTypeEnum.Postgresql) throw new ArgumentException("Select a PostgreSQL destination.", nameof(database));
            DatabaseSettings copy = new DatabaseSettings { Type = database.Type, ConnectionString = database.ConnectionString, ConnectionEnvironmentVariable = database.ConnectionEnvironmentVariable };
            return RestoreAsync(recovery, destination, copy, port, publisher, token);
        }
        private static async Task<RecoveryRestoreResult> RestoreAsync(VerifiedRecovery recovery, string destination, DatabaseSettings targetDatabase, int? port, IRecoveryHomePublisher? publisher, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(recovery);
            string target = Path.GetFullPath(destination);
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Recovery destination already exists. Choose a new application home.");
            string parent = Path.GetDirectoryName(target) ?? throw new ArgumentException("Choose a destination beneath an existing parent directory.");
            if (!Directory.Exists(parent)) { Directory.CreateDirectory(parent); HomePermissions.Directory(parent); }
            string temporary = Path.Combine(parent, "." + Path.GetFileName(target) + ".restore-" + Guid.NewGuid().ToString("N"));
            ArgueBotPaths paths = new ArgueBotPaths(temporary); paths.EnsureDirectories();
            int effectivePort = 0;
            bool published = false; bool externalDatabaseCommitted = false; bool retained = false;
            try
            {
                using (FileStream owner = paths.AcquireLock())
                {
                    string sourceSettings = Path.Combine(recovery.DirectoryPath, "arguebot.json");
                    ServiceSettings settings = ServiceSettings.Parse(await File.ReadAllTextAsync(sourceSettings, token).ConfigureAwait(false));
                    settings.Database = targetDatabase;
                    if (port.HasValue) settings.Port = port.Value;
                    effectivePort = settings.Port;
                    await settings.SaveAsync(paths, token).ConfigureAwait(false);
                    string retainedSettings = Path.Combine(paths.Home, "backups", "source-arguebot.json");
                    File.Copy(sourceSettings, retainedSettings, false); HomePermissions.File(retainedSettings);
                    string retainedSnapshot = Path.Combine(paths.Home, "backups", "restored-database.arguedb.jsonl");
                    File.Copy(Path.Combine(recovery.DirectoryPath, "database.arguedb.jsonl"), retainedSnapshot, false); HomePermissions.File(retainedSnapshot);
                    string manifest = Path.Combine(paths.Home, "backups", "recovery-manifest.json");
                    await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(recovery.Manifest), token).ConfigureAwait(false); HomePermissions.File(manifest);
                    foreach (RecoveryFile key in recovery.Manifest.Files.Where(file => file.Path.StartsWith("keys/", StringComparison.Ordinal)))
                    {
                        string keyDirectory = Path.Combine(paths.Home, "keys"); Directory.CreateDirectory(keyDirectory); HomePermissions.Directory(keyDirectory);
                        string file = Path.Combine(paths.Home, key.Path.Replace('/', Path.DirectorySeparatorChar));
                        File.Copy(Path.Combine(recovery.DirectoryPath, key.Path.Replace('/', Path.DirectorySeparatorChar)), file, false); HomePermissions.File(file);
                    }
                    string incomplete = Path.Combine(paths.Home, "runtime", "recovery-incomplete");
                    if (targetDatabase.Type == DatabaseTypeEnum.Postgresql)
                    {
                        await File.WriteAllTextAsync(incomplete, "PostgreSQL recovery has not completed reconciliation. Keep this home stopped and review the retained snapshot and recovery operation before removing this marker.", token).ConfigureAwait(false);
                        HomePermissions.File(incomplete);
                    }
                    using (DatabaseDriverBase database = targetDatabase.Type == DatabaseTypeEnum.Sqlite ? new SqliteDatabaseDriver(paths) : DatabaseDriverFactory.Create(targetDatabase, Path.Combine(paths.Home, "backups")))
                    {
                        await database.InitializeEmptyAsync(token).ConfigureAwait(false);
                        await database.RestoreSnapshotAsync(retainedSnapshot, token).ConfigureAwait(false);
                        externalDatabaseCommitted = targetDatabase.Type == DatabaseTypeEnum.Postgresql;
                        await database.InitializeAsync(token).ConfigureAwait(false);
                        await database.RevokeRestoredSessionsAsync(token).ConfigureAwait(false);
                    }
                    TokenProtector protector = new TokenProtector(paths);
                    if (recovery.Manifest.IncludesSecrets) protector.Rotate();
                    if (File.Exists(incomplete)) File.Delete(incomplete);
                }
                token.ThrowIfCancellationRequested();
                await (publisher ?? new AtomicRecoveryHomePublisher()).PublishAsync(temporary, target, token).ConfigureAwait(false); published = true;
                return new RecoveryRestoreResult { Home = target, Provider = targetDatabase.Type.ToString(), Port = effectivePort, KeysRestored = recovery.Manifest.IncludesSecrets, SessionsRevoked = true, MissingReferences = recovery.MissingReferences.ToList() };
            }
            catch (Exception exception) when (exception is not OutOfMemoryException && externalDatabaseCommitted)
            {
                retained = true;
                throw new RecoveryPublicationException(temporary, exception);
            }
            finally { if (!published && !retained && Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }
    }
}
