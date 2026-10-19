namespace ArgueBot.Core.Configuration
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed bootstrap settings. Runner and conversation configuration lives in the database.</summary>
    public sealed class ServiceSettings
    {
        private int _Port = 4317;
        /// <summary>Loopback service port, default 4317, allowed 1 through 65535.</summary>
        public int Port { get { return _Port; } set { if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(value)); _Port = value; } }
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
                return JsonSerializer.Deserialize<ServiceSettings>(text) ?? throw new InvalidDataException("Settings cannot be null.");
            }
            ServiceSettings settings = new ServiceSettings();
            await settings.SaveAsync(paths, token).ConfigureAwait(false);
            return settings;
        }
        /// <summary>Atomically replace the settings file.</summary>
        public async Task SaveAsync(ArgueBotPaths paths, CancellationToken token = default)
        {
            if (MaxConcurrentRuns < 1 || MaxConcurrentRuns > 64) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentRuns));
            string temporary = paths.SettingsFile + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, paths.SettingsFile, true);
        }
    }
}
