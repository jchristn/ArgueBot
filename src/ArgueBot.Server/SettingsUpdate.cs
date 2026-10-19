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
        /// <summary>Optional stream batch threshold, one through 65536 characters. Omission preserves the configured value.</summary>
        public int? StreamBatchCharacters { get; set; }
        /// <summary>Optional stream flush interval, ten through 5000 milliseconds. Omission preserves the configured value.</summary>
        public int? StreamCommitMilliseconds { get; set; }
        /// <summary>Optional Unicode code-point limit per message preview, 128 through 16384. Omission preserves its configured value.</summary>
        public int? HistoryPreviewCodePoints { get; set; }
        /// <summary>Optional context character budget, 1024 through 16777216. Omission preserves its configured value.</summary>
        public int? ContextMaxCharacters { get; set; }
        /// <summary>Optional selected context message bound, one through 10000. Omission preserves its configured value.</summary>
        public int? ContextMaxMessages { get; set; }
        /// <summary>Allow local shell-out execution after restart.</summary>
        public bool AllowCli { get; set; }
        /// <summary>Selected persistence provider.</summary>
        public DatabaseTypeEnum DatabaseType { get; set; }
        /// <summary>Server environment reference for the connection string.</summary>
        public string ConnectionEnvironmentVariable { get; set; } = "";
    }
}
