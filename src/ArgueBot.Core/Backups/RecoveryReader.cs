namespace ArgueBot.Core.Backups
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;

    internal static class RecoveryReader
    {
        internal static async Task<VerifiedRecovery> OpenAsync(string source, string? password, RecoveryLimits limits, CancellationToken token)
        {
            if (limits.MaximumBytes < 1 || limits.MaximumBytes > 1099511627776 || limits.MaximumFiles < 3 || limits.MaximumFiles > 65534) throw new ArgumentOutOfRangeException(nameof(limits));
            string workspace = Path.Combine(Path.GetTempPath(), "arguebot-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace); HomePermissions.Directory(workspace);
            bool accepted = false;
            try
            {
                bool encrypted; RecoveryManifest manifest;
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length > limits.MaximumBytes) throw new InvalidDataException("Recovery archive exceeds the configured byte limit.");
                    encrypted = BundleEncryption.IsEncrypted(input);
                    if (encrypted)
                    {
                        if (string.IsNullOrEmpty(password)) throw new ArgumentException("This recovery archive requires its passphrase.");
                        string decrypted = Path.Combine(workspace, "decrypted.zip");
                        using (FileStream output = new FileStream(decrypted, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                        {
                            HomePermissions.File(decrypted);
                            await BundleEncryption.DecryptAsync(input, output, password, limits.MaximumBytes, token).ConfigureAwait(false);
                            output.Position = 0;
                            manifest = await ExtractAsync(output, workspace, encrypted, limits, token).ConfigureAwait(false);
                        }
                        File.Delete(decrypted);
                    }
                    else manifest = await ExtractAsync(input, workspace, false, limits, token).ConfigureAwait(false);
                }
                ServiceSettings settings = ServiceSettings.Parse(await File.ReadAllTextAsync(Path.Combine(workspace, "arguebot.json"), token).ConfigureAwait(false));
                if (!manifest.IncludesSecrets && settings.Database.ConnectionString.Length > 0) throw new InvalidDataException("A default recovery archive contains raw connection configuration.");
                await SnapshotVerifier.ValidateAsync(Path.Combine(workspace, "database.arguedb.jsonl"), token).ConfigureAwait(false);
                await foreach (BackupRecord record in BackupReader.ReadAsync(Path.Combine(workspace, "database.arguedb.jsonl"), token).ConfigureAwait(false))
                {
                    if (record.Kind == "header" && (record.SchemaVersion != manifest.SchemaVersion || record.Provider != manifest.Provider)) throw new InvalidDataException("Recovery manifest and database snapshot disagree.");
                }
                ValidateKeys(workspace, manifest);
                List<string> missing = manifest.SecretReferences.Where(reference => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(reference))).ToList();
                accepted = true; return new VerifiedRecovery(workspace, manifest, settings, encrypted, missing);
            }
            finally { if (!accepted) Directory.Delete(workspace, true); }
        }
        private static async Task<RecoveryManifest> ExtractAsync(Stream source, string workspace, bool encrypted, RecoveryLimits limits, CancellationToken token)
        {
            using (ZipArchive archive = new ZipArchive(source, ZipArchiveMode.Read, true))
            {
                if (archive.Entries.Count < 3 || archive.Entries.Count > limits.MaximumFiles) throw new InvalidDataException("Recovery archive entry count is outside its limit.");
                Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!Allowed(entry.FullName) || !entries.TryAdd(entry.FullName, entry) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Recovery archive contains an unsafe, duplicate or unknown entry.");
                    if (entry.Length < 0 || entry.Length > limits.MaximumBytes - total) throw new InvalidDataException("Recovery content exceeds the configured byte limit.");
                    total += entry.Length;
                    if (entry.FullName.StartsWith("keys/session-", StringComparison.Ordinal) && entry.FullName != "keys/session-current" && entry.Length != 32) throw new InvalidDataException("Recovery session key has an invalid size.");
                    if (entry.FullName == "keys/listener.pfx" && entry.Length > 1048576) throw new InvalidDataException("The listener certificate exceeds the recovery size limit.");
                    if (entry.FullName == "keys/session-current" && entry.Length > 128) throw new InvalidDataException("Recovery key pointer exceeds its size limit.");
                }
                if (!entries.TryGetValue("manifest.json", out ZipArchiveEntry? manifestEntry) || manifestEntry.Length > 4194304) throw new InvalidDataException("Recovery manifest is missing or oversized.");
                RecoveryManifest manifest;
                using (MemoryStream memory = new MemoryStream())
                {
                    using (Stream input = manifestEntry.Open()) { await CopyAsync(input, memory, manifestEntry.Length, token).ConfigureAwait(false); }
                    memory.Position = 0;
                    manifest = await JsonSerializer.DeserializeAsync<RecoveryManifest>(memory, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }, token).ConfigureAwait(false) ?? throw new InvalidDataException("Recovery manifest cannot be null.");
                }
                if (manifest.FormatVersion != 1 || manifest.SchemaVersion < 1 || manifest.SchemaVersion > DatabaseSchema.CurrentVersion || !Enum.TryParse(manifest.Provider, out DatabaseTypeEnum provider) || !Enum.IsDefined(provider) || (manifest.IncludesSecrets && !encrypted)) throw new InvalidDataException("Unsupported recovery format, schema, provider or secret policy.");
                if (manifest.Files.Count != entries.Count - 1 || manifest.SecretReferences.Count > limits.MaximumFiles || manifest.SecretReferences.Count != manifest.SecretReferences.Distinct(StringComparer.Ordinal).Count() || manifest.SecretReferences.Any(reference => !Regex.IsMatch(reference, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant))) throw new InvalidDataException("Recovery manifest inventory or credential references are invalid.");
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RecoveryFile file in manifest.Files)
                {
                    if (file.Path == "manifest.json" || !seen.Add(file.Path) || !Allowed(file.Path) || !entries.TryGetValue(file.Path, out ZipArchiveEntry? entry) || file.Path != entry.FullName || file.Bytes != entry.Length || !Regex.IsMatch(file.Digest, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Recovery entry differs from its manifest.");
                    if (!manifest.IncludesSecrets && file.Path.StartsWith("keys/", StringComparison.Ordinal)) throw new InvalidDataException("A default recovery archive contains encryption keys.");
                    string target = Path.Combine(workspace, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    string parent = Path.GetDirectoryName(target)!; Directory.CreateDirectory(parent); HomePermissions.Directory(parent);
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        HomePermissions.File(target);
                        byte[] digest = await CopyAsync(input, output, file.Bytes, token).ConfigureAwait(false);
                        if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(file.Digest))) throw new InvalidDataException("Recovery entry checksum does not match its manifest.");
                        await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
                    }
                }
                if (!seen.Contains("database.arguedb.jsonl") || !seen.Contains("arguebot.json")) throw new InvalidDataException("Recovery archive lacks its database or settings.");
                return manifest;
            }
        }
        private static async Task<byte[]> CopyAsync(Stream input, Stream output, long expected, CancellationToken token)
        {
            byte[] buffer = new byte[81920]; long total = 0; int count;
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                while ((count = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                {
                    if (count > expected - total) throw new InvalidDataException("Recovery entry exceeds its declared size.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); total += count;
                }
                if (total != expected) throw new InvalidDataException("Recovery entry is truncated.");
                return hash.GetHashAndReset();
            }
        }
        private static bool Allowed(string path)
        { return path == "manifest.json" || path == "database.arguedb.jsonl" || path == "arguebot.json" || path == "keys/session-current" || path == "keys/listener.pfx" || Regex.IsMatch(path, "^keys/session-[a-f0-9]{16}\\.key$", RegexOptions.CultureInvariant); }
        private static void ValidateKeys(string workspace, RecoveryManifest manifest)
        {
            string keys = Path.Combine(workspace, "keys");
            if (!Directory.Exists(keys)) return;
            if (!manifest.IncludesSecrets) throw new InvalidDataException("Unexpected secret recovery content.");
            string pointer = Path.Combine(keys, "session-current");
            if (!File.Exists(pointer)) throw new InvalidDataException("Recovery key ring has no current-key pointer.");
            string current = File.ReadAllText(pointer).Trim();
            if (!Regex.IsMatch(current, "^[a-f0-9]{16}$", RegexOptions.CultureInvariant) || !File.Exists(Path.Combine(keys, "session-" + current + ".key"))) throw new InvalidDataException("Recovery key ring is incomplete.");
        }
    }
}
