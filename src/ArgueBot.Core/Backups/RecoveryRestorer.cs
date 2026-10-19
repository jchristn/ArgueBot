namespace ArgueBot.Core.Backups
{
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Security;

    /// <summary>Publishes a recovered home only after complete validation and offline database restoration.</summary>
    public static class RecoveryRestorer
    {
        /// <summary>Restore into a new SQLite home without overwriting any existing destination. Source data and configuration copies remain available for rollback/reconfiguration.</summary>
        public static async Task<RecoveryRestoreResult> ToNewHomeAsync(VerifiedRecovery recovery, string destination, int? port = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(recovery);
            string target = Path.GetFullPath(destination);
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Recovery destination already exists. Choose a new application home.");
            string parent = Path.GetDirectoryName(target) ?? throw new ArgumentException("Choose a destination beneath an existing parent directory.");
            if (!Directory.Exists(parent)) { Directory.CreateDirectory(parent); HomePermissions.Directory(parent); }
            string temporary = Path.Combine(parent, "." + Path.GetFileName(target) + ".restore-" + Guid.NewGuid().ToString("N"));
            ArgueBotPaths paths = new ArgueBotPaths(temporary); paths.EnsureDirectories();
            bool published = false;
            try
            {
                using (FileStream owner = paths.AcquireLock())
                {
                    string sourceSettings = Path.Combine(recovery.DirectoryPath, "arguebot.json");
                    ServiceSettings settings = ServiceSettings.Parse(await File.ReadAllTextAsync(sourceSettings, token).ConfigureAwait(false));
                    settings.Database = new DatabaseSettings();
                    if (port.HasValue) settings.Port = port.Value;
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
                    using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                    {
                        await database.InitializeAsync(token).ConfigureAwait(false);
                        await database.RestoreSnapshotAsync(retainedSnapshot, token).ConfigureAwait(false);
                        await database.InitializeAsync(token).ConfigureAwait(false);
                        await database.RevokeRestoredSessionsAsync(token).ConfigureAwait(false);
                    }
                    TokenProtector protector = new TokenProtector(paths);
                    if (recovery.Manifest.IncludesSecrets) protector.Rotate();
                }
                token.ThrowIfCancellationRequested();
                Directory.Move(temporary, target); published = true;
                return new RecoveryRestoreResult { Home = target, KeysRestored = recovery.Manifest.IncludesSecrets, SessionsRevoked = true, MissingReferences = recovery.MissingReferences.ToList() };
            }
            finally { if (!published && Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }
    }
}
