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
        /// <summary>Loopback service port, default 4317, allowed 1 through 65535.</summary>
        public int Port { get { return _Port; } set { if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(value)); _Port = value; } }
        /// <summary>Maximum accumulated stream characters before a durable commit. Default 4096, allowed one through 65536.</summary>
        public int StreamBatchCharacters { get; set; } = 4096;
        /// <summary>Maximum stream timer interval in milliseconds, even when the runner stops emitting. Default 250, allowed ten through 5000.</summary>
        public int StreamCommitMilliseconds { get; set; } = 250;
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
