namespace ArgueBot.Core.Configuration
{
    using System;
    using System.IO;

    /// <summary>Resolves the application home without changing environment variables.</summary>
    public sealed class ArgueBotPaths
    {
        /// <summary>Absolute application home.</summary>
        public string Home { get; }
        /// <summary>Default SQLite file.</summary>
        public string DatabaseFile { get { return Path.Combine(Home, "arguebot.db"); } }
        /// <summary>Service settings file.</summary>
        public string SettingsFile { get { return Path.Combine(Home, "arguebot.json"); } }
        /// <summary>Resolve explicit home, environment override, or OS user profile.</summary>
        public ArgueBotPaths(string? home = null)
        {
            string? selected = home ?? Environment.GetEnvironmentVariable("ARGUEBOT_HOME");
            Home = Path.GetFullPath(string.IsNullOrWhiteSpace(selected)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".arguebot") : selected);
        }
        /// <summary>Create owner-only directories on Unix; does not touch an existing home otherwise.</summary>
        public void EnsureDirectories()
        {
            foreach (string directory in new[] { Home, Path.Combine(Home, "runtime"), Path.Combine(Home, "logs"), Path.Combine(Home, "crash-logs"), Path.Combine(Home, "backups"), Path.Combine(Home, "exports"), Path.Combine(Home, "tmp") })
            {
                Directory.CreateDirectory(directory);
                HomePermissions.Directory(directory);
            }
        }
        /// <summary>Acquire a process-held exclusive lock, not a stale PID-file check.</summary>
        public FileStream AcquireLock()
        {
            EnsureDirectories();
            return new FileStream(Path.Combine(Home, "runtime", "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
    }
}
