namespace Test.Shared
{
    using ArgueBot.Publisher;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>Distribution validation shared by the console and both test adapters.</summary>
    public static class PackagingSuites
    {
        /// <summary>Packaging checks that do not require native packagers or external credentials.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Packaging", "Distribution integrity", new List<TestCaseDescriptor> { new TestCaseDescriptor("Packaging", "Integrity", "Distribution refuses unsupported targets, preserves versions and replaces stale checksum records", CheckAsync) }) }; }
        }
        private static async Task CheckAsync(CancellationToken token)
        {
            string directory = Path.Combine(Path.GetTempPath(), "arguebot-packaging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>", token).ConfigureAwait(false);
                PublisherManifest manifest = new PublisherManifest { RuntimeIdentifiers = new List<string> { "linux-x64" }, Projects = new List<string> { "ArgueBot.Server", "ArgueBot.Cli", "ArgueBot.Tui" }, Channels = new Dictionary<string, List<string>> { ["archive"] = new List<string> { "windows", "macos", "linux" } } };
                PackageBuilder builder = new PackageBuilder(directory, manifest); builder.Validate("linux-x64", "archive");
                if (builder.Version() != "2.1.0") throw new InvalidDataException("Packaging changed the declared version.");
                try { builder.Validate("../escape", "archive"); throw new InvalidDataException("An unsupported runtime was accepted."); }
                catch (ArgumentException) { }
                try { builder.Validate("linux-x64", "publish"); throw new InvalidDataException("A build command accepted an undefined publication channel."); }
                catch (ArgumentException) { }
                string artifact = Path.Combine(directory, "arguebot-fixture.zip");
                await File.WriteAllTextAsync(artifact, "first fictional archive", token).ConfigureAwait(false); await PackageBuilder.WriteChecksumAsync(artifact, token).ConfigureAwait(false);
                await File.WriteAllTextAsync(artifact, "replacement fictional archive", token).ConfigureAwait(false); await PackageBuilder.WriteChecksumAsync(artifact, token).ConfigureAwait(false);
                string[] rows = await File.ReadAllLinesAsync(Path.Combine(directory, "SHA256SUMS"), token).ConfigureAwait(false);
                string expected = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(artifact, token).ConfigureAwait(false))).ToLowerInvariant();
                if (rows.Length != 1 || rows[0] != expected + "  arguebot-fixture.zip") throw new InvalidDataException("A rebuilt artifact retained an obsolete checksum.");
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
