namespace ArgueBot.Core.Backups
{
    using System.Collections.Generic;
    using System.IO.Compression;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;

    /// <summary>Complete, bounded recovery archives with an allowlisted inventory and optional authenticated encryption.</summary>
    public static class RecoveryArchive
    {
        /// <summary>Verify and stage every entry before returning data. Dispose the result to remove decrypted temporary files.</summary>
        public static Task<VerifiedRecovery> OpenAsync(string source, string? password = null, RecoveryLimits? limits = null, CancellationToken token = default)
        { return RecoveryReader.OpenAsync(source, password, limits ?? new RecoveryLimits(), token); }
        /// <summary>Create an archive. Secret-inclusive archives require a passphrase; credentials remain references by default.</summary>
        public static async Task<RecoveryManifest> CreateAsync(DatabaseDriverBase database, ArgueBotPaths paths, string destination, bool includeSecrets = false, string? password = null, IReadOnlyList<DependencyReference>? dependencies = null, RecoveryLimits? limits = null, CancellationToken token = default)
        {
            if (includeSecrets && string.IsNullOrEmpty(password)) throw new ArgumentException("Including encryption keys requires a recovery passphrase.");
            if (File.Exists(destination)) throw new IOException("Recovery destination already exists.");
            string workspace = Path.Combine(paths.Home, "tmp", "bundle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace); HomePermissions.Directory(workspace);
            string staged = Path.Combine(workspace, "data"); Directory.CreateDirectory(staged); HomePermissions.Directory(staged);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await database.ExportSnapshotAsync(Path.Combine(staged, "database.arguedb.jsonl"), token).ConfigureAwait(false);
                byte[] configuration = await File.ReadAllBytesAsync(paths.SettingsFile, token).ConfigureAwait(false);
                ServiceSettings settings = ServiceSettings.Parse(Encoding.UTF8.GetString(configuration));
                string revision = "";
                CryptographicOperations.ZeroMemory(configuration);
                if (!includeSecrets) settings.Database.ConnectionString = "";
                ArgueBotPaths stagedPaths = new ArgueBotPaths(staged); await settings.SaveAsync(stagedPaths, token).ConfigureAwait(false);
                RecoveryManifest manifest = new RecoveryManifest { SchemaVersion = DatabaseSchema.CurrentVersion, Provider = settings.Database.Type.ToString(), ApplicationVersion = typeof(RecoveryArchive).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown", CreatedUtc = DateTime.UtcNow, IncludesSecrets = includeSecrets, ConfigurationRevision = revision, Dependencies = dependencies?.ToList() ?? new List<DependencyReference>() };
                if (settings.Database.ConnectionEnvironmentVariable.Length > 0) manifest.SecretReferences.Add(settings.Database.ConnectionEnvironmentVariable);
                int secretColumn = -1;
                await foreach (BackupRecord record in BackupReader.ReadAsync(Path.Combine(staged, "database.arguedb.jsonl"), token).ConfigureAwait(false))
                {
                    if (record.Kind == "header") { manifest.Provider = record.Provider; manifest.SchemaVersion = record.SchemaVersion; }
                    if (record.Kind == "table" && record.Table == "profiles") secretColumn = record.Columns.IndexOf("secret_env");
                    if (record.Kind == "row" && record.Table == "profiles" && secretColumn >= 0)
                    {
                        string? reference = record.Values[secretColumn];
                        if (!string.IsNullOrEmpty(reference) && !manifest.SecretReferences.Contains(reference, StringComparer.Ordinal)) manifest.SecretReferences.Add(reference);
                    }
                }
                if (includeSecrets && Directory.Exists(Path.Combine(paths.Home, "keys")))
                {
                    Directory.CreateDirectory(Path.Combine(staged, "keys")); HomePermissions.Directory(Path.Combine(staged, "keys"));
                    foreach (string key in Directory.GetFiles(Path.Combine(paths.Home, "keys")))
                    {
                        string name = Path.GetFileName(key);
                        if (!Regex.IsMatch(name, "^session-[a-f0-9]{16}\\.key$", RegexOptions.CultureInvariant)) continue;
                        string target = Path.Combine(staged, "keys", name); File.Copy(key, target, false); HomePermissions.File(target);
                    }
                    string current = File.ReadAllText(Path.Combine(paths.Home, "keys", "session-current")).Trim();
                    if (!Regex.IsMatch(current, "^[a-f0-9]{16}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("The source session-key pointer is invalid.");
                    string selected = Path.Combine(staged, "keys", "session-" + current + ".key");
                    if (!File.Exists(selected)) { File.Copy(Path.Combine(paths.Home, "keys", "session-" + current + ".key"), selected, false); HomePermissions.File(selected); }
                    string pointer = Path.Combine(staged, "keys", "session-current"); await File.WriteAllTextAsync(pointer, current, token).ConfigureAwait(false); HomePermissions.File(pointer);
                }
                List<string> files = new List<string> { Path.Combine(staged, "database.arguedb.jsonl"), stagedPaths.SettingsFile };
                if (Directory.Exists(Path.Combine(staged, "keys"))) files.AddRange(Directory.GetFiles(Path.Combine(staged, "keys")).OrderBy(file => file, StringComparer.Ordinal));
                foreach (string file in files)
                {
                    using (FileStream stream = File.OpenRead(file))
                    { manifest.Files.Add(new RecoveryFile { Path = Path.GetRelativePath(staged, file).Replace('\\', '/'), Bytes = stream.Length, Digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) }); }
                }
                manifest.ConfigurationRevision = manifest.Files.Single(file => file.Path == "arguebot.json").Digest;
                string manifestPath = Path.Combine(staged, "manifest.json");
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest), token).ConfigureAwait(false); HomePermissions.File(manifestPath);
                string zipPath = Path.Combine(workspace, "archive.zip");
                using (FileStream stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    HomePermissions.File(zipPath);
                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    {
                        foreach (string file in files.Append(manifestPath))
                        {
                            ZipArchiveEntry entry = archive.CreateEntry(Path.GetRelativePath(staged, file).Replace('\\', '/'), CompressionLevel.Optimal);
                            using (Stream output = entry.Open())
                            using (FileStream input = File.OpenRead(file)) { await input.CopyToAsync(output, token).ConfigureAwait(false); }
                        }
                    }
                }
                using (FileStream input = File.OpenRead(zipPath))
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    HomePermissions.File(temporary);
                    if (string.IsNullOrEmpty(password)) await input.CopyToAsync(output, token).ConfigureAwait(false);
                    else await BundleEncryption.EncryptAsync(input, output, password, token).ConfigureAwait(false);
                    await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
                }
                using (VerifiedRecovery verified = await OpenAsync(temporary, password, limits, token).ConfigureAwait(false)) { }
                File.Move(temporary, destination, false); return manifest;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); Directory.Delete(workspace, true); }
        }
    }
}
