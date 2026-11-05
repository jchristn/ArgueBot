namespace ArgueBot.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.Formats.Tar;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    /// <summary>Builds isolated self-contained bundles without changing product versions or publishing them.</summary>
    public sealed class PackageBuilder
    {
        private readonly string _Root;
        private readonly PublisherManifest _Manifest;
        /// <summary>Use a repository root and its reviewed manifest.</summary>
        public PackageBuilder(string root, PublisherManifest manifest) { _Root = Path.GetFullPath(root); _Manifest = manifest; }
        /// <summary>Validate a target before invoking a compiler or writing distribution files.</summary>
        public void Validate(string runtime, string channel)
        {
            if (!_Manifest.RuntimeIdentifiers.Contains(runtime, StringComparer.Ordinal)) throw new ArgumentException("Runtime is not listed in publisher.json.");
            if (!_Manifest.Channels.TryGetValue(channel, out List<string>? systems)) throw new ArgumentException("Channel is not listed in publisher.json.");
            string system = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
            if (!systems.Contains(system, StringComparer.Ordinal)) throw new InvalidOperationException("Channel " + channel + " requires a native " + string.Join("/", systems) + " runner.");
            if (channel != "archive" && !(runtime.StartsWith(system == "windows" ? "win-" : system == "macos" ? "osx-" : "linux-", StringComparison.Ordinal))) throw new ArgumentException("Native package and runtime platforms must match.");
            if (_Manifest.Projects.Count != 3 || _Manifest.Projects.Any(project => !Regex.IsMatch(project, @"\AArgueBot\.(Server|Cli|Tui)\z")) || _Manifest.Projects.Distinct().Count() != 3) throw new InvalidDataException("The manifest must bundle the service, CLI and TUI together.");
        }
        /// <summary>Read the approved build version without modifying any version source.</summary>
        public string Version()
        {
            string version = XDocument.Load(Path.Combine(_Root, "Directory.Build.props")).Descendants("Version").Single().Value;
            if (!Regex.IsMatch(version, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?\z")) throw new InvalidDataException("The build version is not a supported semantic version.");
            return version;
        }
        /// <summary>Build all three executable surfaces with their platform-specific locked restore graphs.</summary>
        public async Task<string> BuildAsync(string runtime, string channel, CancellationToken token)
        {
            Validate(runtime, channel);
            List<string> prerequisites = await PackageDoctor.InspectAsync(_Root, channel, token).ConfigureAwait(false);
            if (prerequisites.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, prerequisites));
            if (!File.Exists(Path.Combine(_Root, "dashboard", "dist", "index.html"))) throw new FileNotFoundException("Build the dashboard first: npm --prefix dashboard ci && npm --prefix dashboard run build");
            foreach (string project in new[] { "ArgueBot.Server", "ArgueBot.Cli", "ArgueBot.Tui", "ArgueBot.Core", "ArgueBot.Client", "ArgueBot.Runners" })
                if (!File.Exists(Path.Combine(_Root, "packaging", "locks", runtime, project + ".json"))) throw new FileNotFoundException("Missing reviewed packaging dependency lock for " + runtime + "/" + project + ".json");
            string version = Version();
            string output = Path.Combine(_Root, "installers", version); Directory.CreateDirectory(output);
            string staging = Path.Combine(_Root, ".run", "publisher", runtime, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
            string payload = Path.Combine(staging, "payload"); Directory.CreateDirectory(payload);
            foreach (string project in _Manifest.Projects)
            {
                string published = Path.Combine(staging, project);
                await PublisherProcess.RunAsync("dotnet", new[] { "publish", Path.Combine(_Root, "src", project, project + ".csproj"), "-c", "Release", "-f", _Manifest.Framework, "-r", runtime, "--self-contained", "true", "-p:TargetFrameworks=" + _Manifest.Framework, "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=none", "-p:ArgueBotPackaging=true", "-p:ArgueBotPackageRuntime=" + runtime, "-p:RestoreLockedMode=true", "--artifacts-path", Path.Combine(staging, "build"), "-o", published }, PackageDoctor.SdkDirectory(_Root), token).ConfigureAwait(false);
                foreach (string file in Directory.EnumerateFiles(published, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(payload, Path.GetRelativePath(published, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, true);
                }
            }
            foreach (string document in new[] { "README.md", "LICENSE.md", "CHANGELOG.md" }) File.Copy(Path.Combine(_Root, document), Path.Combine(payload, document));
            File.Copy(Path.Combine(_Root, "assets", "dependencies.json"), Path.Combine(payload, "dependencies.json"));
            if (runtime.StartsWith("win-", StringComparison.Ordinal)) File.Copy(Path.Combine(_Root, "packaging", "windows", "arguebot-web.ps1"), Path.Combine(payload, "arguebot-web.ps1"));
            await File.WriteAllTextAsync(Path.Combine(payload, "UNSIGNED.txt"), "ArgueBot is free, open-source software without a purchased code-signing certificate. Windows may show SmartScreen; review the source and checksums before choosing More info and Run anyway. macOS packages use free ad-hoc signatures, not notarization; review the source and checksums before using Privacy & Security > Open Anyway.\n", token).ConfigureAwait(false);
            if (runtime.StartsWith("osx-", StringComparison.Ordinal) && OperatingSystem.IsMacOS())
                foreach (string project in _Manifest.Projects) await PublisherProcess.RunAsync("codesign", new[] { "--force", "--sign", "-", Path.Combine(payload, project) }, _Root, token).ConfigureAwait(false);
            string basename = _Manifest.Name + "-" + version + "-" + runtime;
            string artifact;
            if (channel == "archive")
            {
                artifact = Path.Combine(output, basename + (runtime.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz"));
                if (File.Exists(artifact)) File.Delete(artifact);
                if (artifact.EndsWith(".zip", StringComparison.Ordinal)) ZipFile.CreateFromDirectory(payload, artifact, CompressionLevel.Optimal, false);
                else
                {
                    using (FileStream stream = File.Create(artifact))
                    using (GZipStream gzip = new GZipStream(stream, CompressionLevel.Optimal))
                        await TarFile.CreateFromDirectoryAsync(payload, gzip, false, token).ConfigureAwait(false);
                }
            }
            else artifact = await NativePackages.CreateAsync(_Root, payload, output, basename, version, runtime, channel, token).ConfigureAwait(false);
            await WriteChecksumAsync(artifact, token).ConfigureAwait(false);
            await File.WriteAllTextAsync(artifact + ".build.json", JsonSerializer.Serialize(new { version, runtime, channel, framework = _Manifest.Framework, selfContained = true, singleFile = true, nativeSmokeTested = false, published = false }, new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
            return artifact;
        }
        /// <summary>Record an artifact digest; never treats a built archive as a tested installation.</summary>
        public static async Task WriteChecksumAsync(string artifact, CancellationToken token)
        {
            byte[] digest;
            using (FileStream stream = File.OpenRead(artifact)) digest = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
            string manifest = Path.Combine(Path.GetDirectoryName(artifact)!, "SHA256SUMS");
            List<string> rows = File.Exists(manifest) ? (await File.ReadAllLinesAsync(manifest, token).ConfigureAwait(false)).Where(row => !row.EndsWith("  " + Path.GetFileName(artifact), StringComparison.Ordinal)).ToList() : new List<string>();
            rows.Add(Convert.ToHexString(digest).ToLowerInvariant() + "  " + Path.GetFileName(artifact)); rows.Sort(StringComparer.Ordinal);
            await File.WriteAllLinesAsync(manifest, rows, token).ConfigureAwait(false);
        }
    }
}
