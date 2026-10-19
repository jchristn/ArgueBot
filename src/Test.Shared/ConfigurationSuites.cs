namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using Touchstone.Core;

    /// <summary>Settings durability and credential-origin validation contracts.</summary>
    public static class ConfigurationSuites
    {
        /// <summary>Configuration descriptors shared by every backend runner.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Configuration", "Safe configuration", new List<TestCaseDescriptor> {
                new TestCaseDescriptor("Configuration", "Permissions", "Application directories and exported files restrict access to the current owner", PermissionsAsync),
                new TestCaseDescriptor("Configuration", "Settings", "Invalid saves preserve the last configuration; unknown file fields fail visibly", SettingsAsync),
                new TestCaseDescriptor("Configuration", "ClientOrigin", "Authenticated client refuses unsafe origins and absolute request URLs", ClientOriginAsync)
            }) };
            }
        }
        private static async Task PermissionsAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-permissions-" + Guid.NewGuid().ToString("N"));
            try
            {
                new ArgueBotPaths(home).EnsureDirectories();
                string file = Path.Combine(home, "fixture.txt"); await File.WriteAllTextAsync(file, "fixture", token).ConfigureAwait(false); HomePermissions.File(file);
                if (OperatingSystem.IsWindows())
                {
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        SecurityIdentifier owner = identity.User!;
                        FileSecurity security = new FileInfo(file).GetAccessControl();
                        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                            if (!rule.IdentityReference.Equals(owner)) throw new InvalidDataException("File ACL grants access to another identity.");
                        if (!security.AreAccessRulesProtected) throw new InvalidDataException("File ACL inherits broader parent access.");
                    }
                }
                else if (File.GetUnixFileMode(file) != (UnixFileMode.UserRead | UnixFileMode.UserWrite) || File.GetUnixFileMode(home) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute)) throw new InvalidDataException("Application state permissions are broader than owner-only.");
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task SettingsAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                ServiceSettings settings = await ServiceSettings.LoadAsync(paths, token).ConfigureAwait(false);
                settings.Port = 4517; await settings.SaveAsync(paths, token).ConfigureAwait(false);
                string saved = await File.ReadAllTextAsync(paths.SettingsFile, token).ConfigureAwait(false);
                settings.RequestHistoryBodyBytes = -1;
                bool denied = false;
                try { await settings.SaveAsync(paths, token).ConfigureAwait(false); }
                catch (ArgumentOutOfRangeException) { denied = true; }
                if (!denied || await File.ReadAllTextAsync(paths.SettingsFile, token).ConfigureAwait(false) != saved) throw new InvalidDataException("Invalid configuration replaced the previous settings.");
                if ((await ServiceSettings.LoadAsync(paths, token).ConfigureAwait(false)).Port != 4517 || Directory.GetFiles(home, "*.tmp").Length != 0) throw new InvalidDataException("Settings did not round trip cleanly.");
                await File.WriteAllTextAsync(paths.SettingsFile, "{\"Port\":4517,\"UnknownFutureSetting\":true}", token).ConfigureAwait(false);
                denied = false;
                try { await ServiceSettings.LoadAsync(paths, token).ConfigureAwait(false); }
                catch (System.Text.Json.JsonException) { denied = true; }
                if (!denied) throw new InvalidDataException("Unknown settings were silently discarded.");
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task ClientOriginAsync(CancellationToken token)
        {
            foreach (string endpoint in new string[] { "http://example.invalid", "https://name:secret@example.invalid", "file:///tmp/service", "https://example.invalid/?token=secret" })
            {
                bool denied = false;
                try { using (ArgueBotClient client = new ArgueBotClient(endpoint)) { } }
                catch (ArgumentException) { denied = true; }
                if (!denied) throw new InvalidDataException("Client accepted an unsafe service endpoint.");
            }
            using (ArgueBotClient client = new ArgueBotClient())
            {
                client.SetToken("fixture-token-not-a-real-session");
                foreach (string path in new string[] { "https://example.invalid/v1.0/api/auth/me", "//example.invalid/health", "\\\\example.invalid\\health" })
                {
                    bool denied = false;
                    try { await client.SendAsync<ApiError>(HttpMethod.Get, path, token: token).ConfigureAwait(false); }
                    catch (ArgumentException) { denied = true; }
                    if (!denied) throw new InvalidDataException("An authenticated request could escape the configured origin.");
                }
            }
        }
    }
}
