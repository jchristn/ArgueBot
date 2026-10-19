namespace ArgueBot.Core.Configuration
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;

    /// <summary>Typed bootstrap settings. Runner and conversation configuration lives in the database.</summary>
    public sealed class ServiceSettings
    {
        private TelemetrySettings _Telemetry = new TelemetrySettings();
        private DatabaseSettings _Database = new DatabaseSettings();

        private int _Port = 4317;
        /// <summary>Process telemetry collection and export settings.</summary>
        public TelemetrySettings Telemetry { get { return _Telemetry; } set { ArgumentNullException.ThrowIfNull(value); _Telemetry = value; } }
        /// <summary>Capture HTTP request history by default.</summary>
        public bool RequestHistoryEnabled { get; set; } = true;
        /// <summary>Capture prefix limit in bytes, default 32768.</summary>
        public int RequestHistoryBodyBytes { get; set; } = 32768;
        /// <summary>Request history retention in days, default thirty.</summary>
        public int RequestHistoryRetentionDays { get; set; } = 30;
        /// <summary>Persistent database configuration. SQLite is the local default.</summary>
        public DatabaseSettings Database { get { return _Database; } set { ArgumentNullException.ThrowIfNull(value); _Database = value; } }
        /// <summary>Enable HTTPS using keys/listener.pfx in the application home. Password login requires HTTPS.</summary>
        public bool TlsEnabled { get; set; }
        /// <summary>Optional environment variable containing the listener PFX password. No literal password is persisted.</summary>
        public string TlsPasswordEnvironmentVariable { get; set; } = "";
        /// <summary>Loopback service port, default 4317, allowed 1 through 65535.</summary>
        public int Port { get { return _Port; } set { if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(value)); _Port = value; } }
        /// <summary>Maximum accumulated stream characters before a durable commit. Default 4096, allowed one through 65536.</summary>
        public int StreamBatchCharacters { get; set; } = 4096;
        /// <summary>Maximum stream timer interval in milliseconds, even when the runner stops emitting. Default 250, allowed ten through 5000.</summary>
        public int StreamCommitMilliseconds { get; set; } = 250;
        /// <summary>Enable background pruning of replay-event prefixes. Transcript and audit history are retained independently. Default true.</summary>
        public bool EventRetentionEnabled { get; set; } = true;
        /// <summary>Replay age target in days, default thirty, range one through 3650. The latest event is always retained to preserve monotonic sequencing.</summary>
        public int EventRetentionDays { get; set; } = 30;
        /// <summary>Maximum retained replay events per conversation, default 10000, range one through 1000000.</summary>
        public int EventMaxPerConversation { get; set; } = 10000;
        /// <summary>Background retention interval in seconds, default sixty, range five through 3600.</summary>
        public int EventMaintenanceSeconds { get; set; } = 60;
        /// <summary>Conversation metadata batch bound for retention, default one hundred, range one through one thousand.</summary>
        public int EventRetentionBatchSize { get; set; } = 100;
        /// <summary>Default events per replay read, default one thousand, range one through one thousand.</summary>
        public int EventReplayPageSize { get; set; } = 1000;
        /// <summary>Maximum Unicode code points in each message preview returned by paged history. Default 4096, range 128 through 16384. Full message reads remain explicit.</summary>
        public int HistoryPreviewCodePoints { get; set; } = 4096;
        /// <summary>Maximum exact UTF-16 characters in the system prompt plus selected message content. Default 262144, range 1024 through 16777216. Oversized context pauses without invoking a model or truncating history.</summary>
        public int ContextMaxCharacters { get; set; } = 262144;
        /// <summary>Maximum selected messages per generation attempt. Default 1000, range one through 10000.</summary>
        public int ContextMaxMessages { get; set; } = 1000;
        /// <summary>Default maximum concurrent conversations.</summary>
        public int MaxConcurrentRuns { get; set; } = 4;
        /// <summary>Whether shell-out runners may execute. Default true for local mode only.</summary>
        public bool AllowCli { get; set; } = true;
        /// <summary>Load or atomically create service settings.</summary>
        public static async Task<ServiceSettings> LoadAsync(ArgueBotPaths paths, CancellationToken token = default)
        {
            paths.EnsureDirectories();
            if (File.Exists(paths.SettingsFile))
            {
                string text = await File.ReadAllTextAsync(paths.SettingsFile, token).ConfigureAwait(false);
                return Parse(text);
            }
            ServiceSettings settings = new ServiceSettings();
            await settings.SaveAsync(paths, token).ConfigureAwait(false);
            return settings;
        }
        /// <summary>Parse and validate a complete settings document without reading or modifying the filesystem.</summary>
        public static ServiceSettings Parse(string text)
        {
            ServiceSettings settings = JsonSerializer.Deserialize<ServiceSettings>(text, SerializerOptions()) ?? throw new InvalidDataException("Settings cannot be null.");
            settings.Validate(); return settings;
        }
        /// <summary>Atomically replace the settings file.</summary>
        public async Task SaveAsync(ArgueBotPaths paths, CancellationToken token = default)
        {
            Validate();
            paths.EnsureDirectories();
            string temporary = paths.SettingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this, SerializerOptions()), token).ConfigureAwait(false);
                HomePermissions.File(temporary);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, paths.SettingsFile, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        /// <summary>Validate settings before they can become the next startup configuration.</summary>
        public void Validate()
        {
            if (TlsPasswordEnvironmentVariable == null || (TlsPasswordEnvironmentVariable.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(TlsPasswordEnvironmentVariable, "^[A-Za-z_][A-Za-z0-9_]{0,127}$"))) throw new ArgumentException("Use a TLS password environment variable name.");
            if (EventRetentionDays < 1 || EventRetentionDays > 3650) throw new ArgumentOutOfRangeException(nameof(EventRetentionDays));
            if (EventMaxPerConversation < 1 || EventMaxPerConversation > 1000000) throw new ArgumentOutOfRangeException(nameof(EventMaxPerConversation));
            if (EventMaintenanceSeconds < 5 || EventMaintenanceSeconds > 3600) throw new ArgumentOutOfRangeException(nameof(EventMaintenanceSeconds));
            if (EventRetentionBatchSize < 1 || EventRetentionBatchSize > 1000) throw new ArgumentOutOfRangeException(nameof(EventRetentionBatchSize));
            if (EventReplayPageSize < 1 || EventReplayPageSize > 1000) throw new ArgumentOutOfRangeException(nameof(EventReplayPageSize));
            if (HistoryPreviewCodePoints < 128 || HistoryPreviewCodePoints > 16384) throw new ArgumentOutOfRangeException(nameof(HistoryPreviewCodePoints));
            if (ContextMaxCharacters < 1024 || ContextMaxCharacters > 16777216) throw new ArgumentOutOfRangeException(nameof(ContextMaxCharacters));
            if (ContextMaxMessages < 1 || ContextMaxMessages > 10000) throw new ArgumentOutOfRangeException(nameof(ContextMaxMessages));
            if (StreamBatchCharacters < 1 || StreamBatchCharacters > 65536) throw new ArgumentOutOfRangeException(nameof(StreamBatchCharacters));
            if (StreamCommitMilliseconds < 10 || StreamCommitMilliseconds > 5000) throw new ArgumentOutOfRangeException(nameof(StreamCommitMilliseconds));
            if (MaxConcurrentRuns < 1 || MaxConcurrentRuns > 64) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentRuns));
            if (RequestHistoryBodyBytes < 0 || RequestHistoryBodyBytes > 1048576) throw new ArgumentOutOfRangeException(nameof(RequestHistoryBodyBytes));
            if (RequestHistoryRetentionDays < 1 || RequestHistoryRetentionDays > 3650) throw new ArgumentOutOfRangeException(nameof(RequestHistoryRetentionDays));
            ArgumentNullException.ThrowIfNull(Database);
            ArgumentNullException.ThrowIfNull(Telemetry);
            if (!Enum.IsDefined(Database.Type)) throw new ArgumentOutOfRangeException(nameof(Database.Type));
            if (Database.ConnectionEnvironmentVariable.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(Database.ConnectionEnvironmentVariable, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new ArgumentException("Use an environment variable name for the database credential reference.");
            if (!Uri.TryCreate(Telemetry.Endpoint, UriKind.Absolute, out Uri? endpoint) || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0) throw new ArgumentException("Telemetry endpoint must be an HTTP URL without embedded credentials.");
        }
        private static JsonSerializerOptions SerializerOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

    }
}
