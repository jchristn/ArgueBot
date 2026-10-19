namespace ArgueBot.Core.Database
{
    using System;
    /// <summary>Provider-neutral database selection. Secrets should be supplied by environment reference.</summary>
    public sealed class DatabaseSettings
    {
        private string _ConnectionString = "";
        private string _ConnectionEnvironmentVariable = "";

        /// <summary>Default embedded provider.</summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Sqlite;
        /// <summary>SQLite path or server connection string without committed secrets.</summary>
        public string ConnectionString { get { return _ConnectionString; } set { ArgumentNullException.ThrowIfNull(value); _ConnectionString = value; } }
        /// <summary>Optional environment variable that overrides ConnectionString.</summary>
        public string ConnectionEnvironmentVariable { get { return _ConnectionEnvironmentVariable; } set { ArgumentNullException.ThrowIfNull(value); _ConnectionEnvironmentVariable = value; } }
    }
}
