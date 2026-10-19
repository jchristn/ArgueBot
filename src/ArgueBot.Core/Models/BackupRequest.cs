namespace ArgueBot.Core.Models
{
    using System;
    using ArgueBot.Core.Database;

    /// <summary>Administrative recovery request. Passphrases are held only for the accepted in-memory operation.</summary>
    public sealed class BackupRequest
    {
        private string _Name = "Recovery archive";
        private string _Password = "";
        private string _RestoreConnectionEnvironmentVariable = "";
        private string _DestinationName = "";

        /// <summary>Display name for a newly created artifact, up to 200 characters.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }
        /// <summary>Include local encryption keys and literal database configuration. Requires a passphrase.</summary>
        public bool IncludeSecrets { get; set; }
        /// <summary>Optional archive passphrase, at least twelve characters. Never persisted in job metadata.</summary>
        public string Password { get { return _Password; } set { ArgumentNullException.ThrowIfNull(value); _Password = value; } }
        /// <summary>New child-directory name beneath the service home's restores directory. Existing homes are never replaced.</summary>
        public string DestinationName { get { return _DestinationName; } set { ArgumentNullException.ThrowIfNull(value); _DestinationName = value; } }
        /// <summary>Destination provider for a new recovered home. SQLite is the default; PostgreSQL requires an empty pre-provisioned database.</summary>
        public DatabaseTypeEnum RestoreDatabaseType { get; set; } = DatabaseTypeEnum.Sqlite;
        /// <summary>Environment reference on the service machine containing the PostgreSQL destination connection string. Never send its secret value.</summary>
        public string RestoreConnectionEnvironmentVariable { get { return _RestoreConnectionEnvironmentVariable; } set { ArgumentNullException.ThrowIfNull(value); _RestoreConnectionEnvironmentVariable = value; } }
        /// <summary>Listener port for a new recovered home. Zero preserves the archived setting.</summary>
        public int Port { get; set; }
    }
}
