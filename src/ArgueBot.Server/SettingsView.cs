namespace ArgueBot.Server
{
    using ArgueBot.Core.Configuration;

    /// <summary>Redacted configured settings with optimistic revision and application metadata.</summary>
    public sealed class SettingsView
    {
        /// <summary>Editable nonsecret settings. Connection strings are never returned.</summary>
        public ServiceSettings Settings { get; set; } = new ServiceSettings();
        /// <summary>Digest of the current settings file for optimistic writes.</summary>
        public string Revision { get; set; } = "";
        /// <summary>Whether this file differs from the running configuration.</summary>
        public bool RestartRequired { get; set; }
        /// <summary>Whether the running port is overridden by an explicit command-line flag.</summary>
        public bool PortOverridden { get; set; }
        /// <summary>Effective port of this process.</summary>
        public int EffectivePort { get; set; }
        /// <summary>Whether a private database connection string is configured.</summary>
        public bool ConnectionConfigured { get; set; }
    }
}
