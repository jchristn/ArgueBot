namespace ArgueBot.Server
{
    using ArgueBot.Core.Database;

    /// <summary>Allowlisted nonsecret settings update. Raw credential values cannot be submitted here.</summary>
    public sealed class SettingsUpdate
    {
        /// <summary>Expected settings file revision.</summary>
        public string Revision { get; set; } = "";
        /// <summary>Configured port, from 1 to 65535. Explicit launch flags still win.</summary>
        public int Port { get; set; }
        /// <summary>Concurrent conversations, 1 to 64.</summary>
        public int MaxConcurrentRuns { get; set; }
        /// <summary>Allow local shell-out execution after restart.</summary>
        public bool AllowCli { get; set; }
        /// <summary>Selected persistence provider.</summary>
        public DatabaseTypeEnum DatabaseType { get; set; }
        /// <summary>Server environment reference for the connection string.</summary>
        public string ConnectionEnvironmentVariable { get; set; } = "";
    }
}
