namespace ArgueBot.Server.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Backups;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;

    internal static class OfflineMaintenance
    {
        internal static async Task ExecuteAsync(ArgueBotPaths paths, string file, bool restore, CancellationToken token)
        {
            if (restore) await SnapshotVerifier.ValidateAsync(file, token).ConfigureAwait(false);
            using (FileStream owner = paths.AcquireLock())
            {
                ServiceSettings settings = await ServiceSettings.LoadAsync(paths, token).ConfigureAwait(false);
                using (DatabaseDriverBase database = settings.Database.Type == DatabaseTypeEnum.Sqlite && settings.Database.ConnectionString.Length == 0 && settings.Database.ConnectionEnvironmentVariable.Length == 0 ? new SqliteDatabaseDriver(paths) : DatabaseDriverFactory.Create(settings.Database, Path.Combine(paths.Home, "backups")))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    if (restore)
                    {
                        await database.ExportSnapshotAsync(Path.Combine(paths.Home, "backups", "before-restore-" + Guid.NewGuid().ToString("N") + ".arguedb.jsonl"), token).ConfigureAwait(false);
                        await database.RestoreSnapshotAsync(file, token).ConfigureAwait(false);
                        await database.InitializeAsync(token).ConfigureAwait(false);
                        await database.RevokeRestoredSessionsAsync(token).ConfigureAwait(false);
                    }
                    else await database.ExportSnapshotAsync(file, token).ConfigureAwait(false);
                }
            }
        }
    }
}
