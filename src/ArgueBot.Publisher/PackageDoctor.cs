namespace ArgueBot.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Checks build prerequisites without producing or publishing a package.</summary>
    public static class PackageDoctor
    {
        /// <summary>Report actionable prerequisites for the selected native channel and reviewed SDK graph.</summary>
        public static async Task<List<string>> InspectAsync(string root, string channel, CancellationToken token)
        {
            List<string> problems = new List<string>();
            using (JsonDocument sdk = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "global.json"), token).ConfigureAwait(false)))
            using (Process process = new Process { StartInfo = new ProcessStartInfo("dotnet", "--version") { WorkingDirectory = SdkDirectory(root), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } })
            {
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync(token); Task<string> error = process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token).ConfigureAwait(false); await error.ConfigureAwait(false);
                string version = (await output.ConfigureAwait(false)).Trim();
                string expected = sdk.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
                if (process.ExitCode != 0 || version != expected) problems.Add("Packaging requires the reviewed .NET SDK " + expected + "; active SDK is " + version + ". Install that exact SDK or deliberately refresh and review packaging dependency locks for a new SDK.");
            }
            if (!File.Exists(Path.Combine(root, "dashboard", "dist", "index.html"))) problems.Add("Build dashboard assets with npm --prefix dashboard ci and npm --prefix dashboard run build.");
            if (channel == "inno" && !Available("iscc")) problems.Add("Run node scripts/install-package-tools.mjs win-x64 and add .run/package-tools/inno to PATH.");
            if ((channel == "deb" || channel == "rpm") && !Available("fpm")) problems.Add("Install Ruby development tools and rpm, then gem install --no-document fpm -v 1.17.0.");
            if (channel == "pkg" && (!Available("pkgbuild") || !Available("codesign"))) problems.Add("Install Apple's command-line tools with xcode-select --install.");
            if (channel == "appimage" && (!Available(Environment.GetEnvironmentVariable("ARGUEBOT_APPIMAGETOOL") ?? "appimagetool") || !File.Exists(Environment.GetEnvironmentVariable("ARGUEBOT_APPIMAGE_RUNTIME")))) problems.Add("Run node scripts/install-package-tools.mjs for the target Linux runtime and set its printed ARGUEBOT_APPIMAGETOOL and ARGUEBOT_APPIMAGE_RUNTIME variables.");
            if (channel == "appimage" && (!Available("file") || !Available("desktop-file-validate") || !Available("mksquashfs"))) problems.Add("Install file, desktop-file-utils and squashfs-tools with the Linux package manager.");
            return problems;
        }
        internal static string SdkDirectory(string root)
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json"))))
            {
                string directory = Path.Combine(root, ".run", "publisher", "sdk"); Directory.CreateDirectory(directory);
                string content = JsonSerializer.Serialize(new { sdk = new { version = document.RootElement.GetProperty("sdk").GetProperty("version").GetString(), rollForward = "disable" } });
                string file = Path.Combine(directory, "global.json");
                if (!File.Exists(file) || File.ReadAllText(file) != content) File.WriteAllText(file, content);
                return directory;
            }
        }
        private static bool Available(string executable)
        {
            if (Path.IsPathRooted(executable)) return File.Exists(executable);
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(directory, executable))) return true;
                if (OperatingSystem.IsWindows() && File.Exists(Path.Combine(directory, executable + ".exe"))) return true;
            }
            return false;
        }
    }
}
