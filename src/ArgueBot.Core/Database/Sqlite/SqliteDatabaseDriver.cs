namespace ArgueBot.Core.Database.Sqlite
{
    using System;
    using System.Data.Common;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Configuration;

    /// <summary>SQLite implementation with WAL, serialized writes and online backup.</summary>
    public sealed class SqliteDatabaseDriver : DatabaseDriverBase
    {
        private int _PermissionsApplied;
        /// <summary>Create a driver for an isolated or user application home.</summary>
        public SqliteDatabaseDriver(ArgueBotPaths paths) : this(FromPaths(paths)) { }
        /// <summary>Create a driver with explicit SQLite settings.</summary>
        public SqliteDatabaseDriver(DatabaseSettings settings) : base(settings) { }
        private static DatabaseSettings FromPaths(ArgueBotPaths paths)
        {
            paths.EnsureDirectories();
            return new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, ConnectionString = new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, ForeignKeys = true, DefaultTimeout = 5, Pooling = false }.ToString() };
        }
        /// <inheritdoc />
        protected override void OnConnectionOpened(DbConnection connection)
        {
            string file = ((SqliteConnection)connection).DataSource;
            if (Interlocked.CompareExchange(ref _PermissionsApplied, 1, 0) == 0 && File.Exists(file)) HomePermissions.File(file);
        }
        /// <inheritdoc />
        protected override async Task BeforeUpgradeAsync(long version, CancellationToken token)
        {
            using (SqliteConnection connection = (SqliteConnection)await OpenAsync(token).ConfigureAwait(false))
            {
                string folder = Path.Combine(Path.GetDirectoryName(connection.DataSource)!, "backups");
                Directory.CreateDirectory(folder);
                await BackupAsync(Path.Combine(folder, "before-schema-" + version + "-" + Guid.NewGuid().ToString("N") + ".db"), token).ConfigureAwait(false);
            }
        }
        /// <inheritdoc />
        public override async Task BackupAsync(string destination, CancellationToken token = default)
        {
            if (File.Exists(destination)) throw new IOException("Backup destination already exists.");
            using (SqliteConnection source = (SqliteConnection)await OpenAsync(token).ConfigureAwait(false))
            using (SqliteConnection target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()))
            {
                await target.OpenAsync(token).ConfigureAwait(false); HomePermissions.File(destination); source.BackupDatabase(target);
            }
            HomePermissions.File(destination);
        }
    }
}
