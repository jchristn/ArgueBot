namespace ArgueBot.Core.Database.Postgresql
{
    using System;
    using System.Data.Common;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;

    /// <summary>PostgreSQL relational driver. A nonpooled advisory-lock session owns every write and is never transparently reconnected.</summary>
    public sealed class PostgresqlDatabaseDriver : DatabaseDriverBase
    {
        private readonly DatabaseSettings _Settings;
        /// <summary>Create without implicitly provisioning a database or changing credentials.</summary>
        public PostgresqlDatabaseDriver(DatabaseSettings settings, string? backupDirectory = null) : base(settings, backupDirectory) { _Settings = settings; }
        /// <inheritdoc />
        protected override async Task<DbConnection?> AcquireOwnedWriterAsync(CancellationToken token)
        {
            string value = _Settings.ConnectionEnvironmentVariable.Length > 0 ? Environment.GetEnvironmentVariable(_Settings.ConnectionEnvironmentVariable) ?? throw new InvalidOperationException("Database credential reference is not set.") : _Settings.ConnectionString;
            NpgsqlConnectionStringBuilder options = new NpgsqlConnectionStringBuilder(value) { Pooling = false, KeepAlive = 5, ApplicationName = "ArgueBot exclusive writer" };
            NpgsqlConnection connection = new NpgsqlConnection(options.ConnectionString);
            try
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand command = new NpgsqlCommand("SELECT pg_try_advisory_lock(4715949817048297332)", connection))
                {
                    if (!(bool)(await command.ExecuteScalarAsync(token).ConfigureAwait(false) ?? false)) throw new IOException("Another ArgueBot service owns this PostgreSQL database. Attach to that service or choose a different database.");
                }
                return connection;
            }
            catch { connection.Dispose(); throw; }
        }
    }
}
