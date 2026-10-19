namespace ArgueBot.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Database.Mysql;
    using ArgueBot.Core.Database.Postgresql;
    using ArgueBot.Core.Database.SqlServer;
    using ArgueBot.Core.Database.Sqlite;
    using MySqlConnector;
    using Npgsql;

    /// <summary>Composition root for the complete database provider matrix.</summary>
    public static class DatabaseDriverFactory
    {
        /// <summary>Create a driver without mutating its schema.</summary>
        public static DatabaseDriverBase Create(DatabaseSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return settings.Type switch
            {
                DatabaseTypeEnum.Sqlite => new SqliteDatabaseDriver(settings),
                DatabaseTypeEnum.Mysql => new MysqlDatabaseDriver(settings),
                DatabaseTypeEnum.Postgresql => new PostgresqlDatabaseDriver(settings),
                DatabaseTypeEnum.SqlServer => new SqlServerDatabaseDriver(settings),
                _ => throw new ArgumentOutOfRangeException(nameof(settings))
            };
        }
        /// <summary>Create and initialize, disposing on initialization failure.</summary>
        public static async Task<DatabaseDriverBase> CreateAndInitializeAsync(DatabaseSettings settings, CancellationToken token = default)
        {
            DatabaseDriverBase database = Create(settings);
            try { await database.InitializeAsync(token).ConfigureAwait(false); return database; }
            catch { database.Dispose(); throw; }
        }
        internal static DbConnection CreateConnection(DatabaseSettings settings)
        {
            string connection = settings.ConnectionEnvironmentVariable.Length > 0
                ? Environment.GetEnvironmentVariable(settings.ConnectionEnvironmentVariable) ?? throw new InvalidOperationException("Database credential environment reference is not set.")
                : settings.ConnectionString;
            return settings.Type switch
            {
                DatabaseTypeEnum.Sqlite => new SqliteConnection(connection),
                DatabaseTypeEnum.Mysql => new MySqlConnection(connection),
                DatabaseTypeEnum.Postgresql => new NpgsqlConnection(connection),
                DatabaseTypeEnum.SqlServer => new SqlConnection(connection),
                _ => throw new ArgumentOutOfRangeException(nameof(settings))
            };
        }
    }
}
