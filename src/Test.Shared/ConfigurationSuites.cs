namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Security.AccessControl;
    using System.Security.Cryptography;
    using System.Security.Principal;
    using System.Text.Json;
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
                new TestCaseDescriptor("Configuration", "HomeBinding", "Local routing identities are atomic, path-bound and read-only when absent", HomeBindingAsync),
                new TestCaseDescriptor("Configuration", "Permissions", "Application directories and exported files restrict access to the current owner", PermissionsAsync),
                new TestCaseDescriptor("Configuration", "Settings", "Invalid saves preserve the last configuration; unknown file fields fail visibly", SettingsAsync),
                new TestCaseDescriptor("Configuration", "ClientOrigin", "Client refuses unsafe origins and absolute request URLs", ClientOriginAsync)
            }) };
            }
        }
        private static async Task HomeBindingAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-binding-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(Path.Combine(home, "source"));
                if (LocalHomeIdentity.Read(paths) != null || Directory.Exists(home)) throw new InvalidDataException("Read-only identity lookup created a home.");
                Task<string>[] clients = new Task<string>[8];
                for (int index = 0; index < clients.Length; index++) clients[index] = Task.Run(() => LocalHomeIdentity.Ensure(paths), token);
                string[] bindings = await Task.WhenAll(clients).ConfigureAwait(false);
                for (int round = 0; round < 4; round++)
                {
                    ArgueBotPaths raced = new ArgueBotPaths(Path.Combine(home, "race-" + round));
                    Task<string>[] concurrent = new Task<string>[8];
                    for (int index = 0; index < concurrent.Length; index++) concurrent[index] = Task.Run(() => LocalHomeIdentity.Ensure(raced), token);
                    string[] identities = await Task.WhenAll(concurrent).ConfigureAwait(false);
                    foreach (string identity in identities) if (identity != identities[0]) throw new InvalidDataException("Concurrent creation replaced an already published private home key.");
                }
                foreach (string binding in bindings) if (binding != bindings[0] || binding.Length != 64) throw new InvalidDataException("Concurrent local clients did not share a complete routing identity.");
                if (LocalHomeIdentity.Read(new ArgueBotPaths(paths.Home + Path.DirectorySeparatorChar)) != bindings[0]) throw new InvalidDataException("A trailing home separator changed routing identity.");
                ArgueBotPaths copied = new ArgueBotPaths(Path.Combine(home, "copied")); copied.EnsureDirectories();
                File.Copy(Path.Combine(paths.Home, "runtime", "home-identity"), Path.Combine(copied.Home, "runtime", "home-identity"));
                if (LocalHomeIdentity.Read(copied) == bindings[0]) throw new InvalidDataException("A copied home retained another path's routing identity.");
                string challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                string proof = LocalHomeIdentity.CreateProof(paths, 4567, challenge);
                if (!LocalHomeIdentity.VerifyProof(paths, 4567, challenge, proof) || LocalHomeIdentity.VerifyProof(paths, 4568, challenge, proof) || LocalHomeIdentity.VerifyProof(copied, 4567, challenge, proof) || LocalHomeIdentity.VerifyProof(paths, 4567, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), proof)) throw new InvalidDataException("Local process proofs did not bind the private home key, actual listener port and fresh challenge.");
                await File.WriteAllTextAsync(Path.Combine(copied.Home, "runtime", "recovery-incomplete"), "fixture incomplete recovery", token).ConfigureAwait(false);
                bool stopped = false;
                try { await LocalServiceConnector.ConnectAsync(copied.Home, token: token).ConfigureAwait(false); }
                catch (InvalidOperationException exception) when (exception.Message.Contains("operator review", StringComparison.Ordinal)) { stopped = true; }
                if (!stopped) throw new InvalidDataException("An incomplete recovery home could auto-start.");
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
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
                ServiceSettings legacy = ServiceSettings.Parse("{\"port\":4517,\"telemetry\":{\"enabled\":true,\"exportEnabled\":true,\"endpoint\":\"http://127.0.0.1:1\"}}");
                await legacy.SaveAsync(paths, token).ConfigureAwait(false);
                if ((await File.ReadAllTextAsync(paths.SettingsFile, token).ConfigureAwait(false)).Contains("telemetry", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Retired telemetry settings were persisted.");
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
                foreach (string path in new string[] { "https://example.invalid/v1.0/api/conversations", "//example.invalid/health", "\\\\example.invalid\\health" })
                {
                    bool denied = false;
                    try { await client.SendAsync<ApiError>(HttpMethod.Get, path, token: token).ConfigureAwait(false); }
                    catch (ArgumentException) { denied = true; }
                    if (!denied) throw new InvalidDataException("A request could escape the configured origin.");
                }
            }
        }
    }
}
