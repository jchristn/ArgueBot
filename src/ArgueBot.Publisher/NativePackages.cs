namespace ArgueBot.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class NativePackages
    {
        internal static async Task<string> CreateAsync(string root, string payload, string output, string basename, string version, string runtime, string channel, CancellationToken token)
        {
            string nativeOutput = Path.Combine(Path.GetDirectoryName(payload)!, "native-output"); Directory.CreateDirectory(nativeOutput);
            string artifact = Path.Combine(nativeOutput, basename + (channel == "inno" ? "-setup.exe" : channel == "appimage" ? ".AppImage" : "." + channel));
            if (channel == "inno")
            {
                await PublisherProcess.RunAsync("iscc", new[] { "/DSource=" + payload, "/DOutput=" + nativeOutput, "/DArtifact=" + basename + "-setup", "/DVersion=" + version, "/DArchitecture=" + (runtime.EndsWith("arm64", StringComparison.Ordinal) ? "arm64" : "x64compatible"), Path.Combine(root, "packaging", "windows", "arguebot.iss") }, root, token).ConfigureAwait(false);
            }
            else if (channel == "pkg" && OperatingSystem.IsMacOS())
            {
                string app = Path.Combine(Path.GetDirectoryName(payload)!, "ArgueBot.app");
                string contents = Path.Combine(app, "Contents"); string binaries = Path.Combine(contents, "MacOS");
                Copy(payload, binaries); Directory.CreateDirectory(Path.Combine(contents, "Resources"));
                string iconset = Path.Combine(Path.GetDirectoryName(payload)!, "ArgueBot.iconset"); Directory.CreateDirectory(iconset);
                foreach (int size in new[] { 16, 32, 128, 256, 512 })
                foreach (int scale in new[] { 1, 2 })
                {
                    string pixels = (size * scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string icon = Path.Combine(iconset, "icon_" + size + "x" + size + (scale == 2 ? "@2x" : "") + ".png");
                    await PublisherProcess.RunAsync("sips", new[] { "-z", pixels, pixels, Path.Combine(root, "icon.png"), "--out", icon }, root, token).ConfigureAwait(false);
                }
                await PublisherProcess.RunAsync("iconutil", new[] { "-c", "icns", iconset, "--output", Path.Combine(contents, "Resources", "ArgueBot.icns") }, root, token).ConfigureAwait(false);
                string plist = (await File.ReadAllTextAsync(Path.Combine(root, "packaging", "macos", "Info.plist"), token).ConfigureAwait(false)).Replace("@VERSION@", version, StringComparison.Ordinal);
                await File.WriteAllTextAsync(Path.Combine(contents, "Info.plist"), plist, token).ConfigureAwait(false);
                File.Copy(Path.Combine(root, "packaging", "macos", "arguebot-web"), Path.Combine(binaries, "arguebot-web"));
                File.SetUnixFileMode(Path.Combine(binaries, "arguebot-web"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                await PublisherProcess.RunAsync("codesign", new[] { "--force", "--deep", "--sign", "-", app }, root, token).ConfigureAwait(false);
                await PublisherProcess.RunAsync("pkgbuild", new[] { "--component", app, "--install-location", "/Applications", "--identifier", "com.joelchristner.arguebot", "--version", version, artifact }, root, token).ConfigureAwait(false);
            }
            else if (OperatingSystem.IsLinux())
            {
                string directory = Path.Combine(Path.GetDirectoryName(payload)!, "linux-root");
                Copy(payload, Path.Combine(directory, "opt", "arguebot"));
                string webLauncher = Path.Combine(directory, "opt", "arguebot", "arguebot-web");
                File.Copy(Path.Combine(root, "packaging", "linux", "arguebot-web"), webLauncher);
                File.SetUnixFileMode(webLauncher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                string commands = Path.Combine(directory, "usr", "bin"); Directory.CreateDirectory(commands);
                foreach (string suffix in new[] { "cli", "tui", "server" })
                {
                    string project = suffix == "cli" ? "Cli" : suffix == "tui" ? "Tui" : "Server";
                    string file = Path.Combine(commands, suffix == "cli" ? "arguebot" : "arguebot-" + suffix);
                    await File.WriteAllTextAsync(file, "#!/bin/sh\nexec /opt/arguebot/ArgueBot." + project + " \"$@\"\n", token).ConfigureAwait(false);
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                string webCommand = Path.Combine(commands, "arguebot-web");
                await File.WriteAllTextAsync(webCommand, "#!/bin/sh\nexec /opt/arguebot/arguebot-web \"$@\"\n", token).ConfigureAwait(false);
                File.SetUnixFileMode(webCommand, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                string applications = Path.Combine(directory, "usr", "share", "applications"); Directory.CreateDirectory(applications);
                string terminalDesktop = (await File.ReadAllTextAsync(Path.Combine(root, "packaging", "linux", "arguebot.desktop"), token).ConfigureAwait(false)).Replace("Exec=arguebot\n", "Exec=arguebot-tui\n", StringComparison.Ordinal);
                await File.WriteAllTextAsync(Path.Combine(applications, "arguebot.desktop"), terminalDesktop, token).ConfigureAwait(false);
                File.Copy(Path.Combine(root, "packaging", "linux", "arguebot-web.desktop"), Path.Combine(applications, "arguebot-web.desktop"));
                string icons = Path.Combine(directory, "usr", "share", "pixmaps"); Directory.CreateDirectory(icons);
                File.Copy(Path.Combine(root, "icon.png"), Path.Combine(icons, "arguebot.png"));
                string units = Path.Combine(directory, "usr", "lib", "systemd", "user"); Directory.CreateDirectory(units);
                File.Copy(Path.Combine(root, "packaging", "linux", "arguebot.service"), Path.Combine(units, "arguebot.service"));
                if (channel == "appimage")
                {
                    File.Copy(Path.Combine(root, "packaging", "linux", "AppRun"), Path.Combine(directory, "AppRun"));
                    File.SetUnixFileMode(Path.Combine(directory, "AppRun"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    File.Copy(Path.Combine(root, "packaging", "linux", "arguebot.desktop"), Path.Combine(directory, "arguebot.desktop"));
                    File.Copy(Path.Combine(root, "icon.png"), Path.Combine(directory, "arguebot.png"));
                    string runtimeFile = Environment.GetEnvironmentVariable("ARGUEBOT_APPIMAGE_RUNTIME") ?? throw new InvalidOperationException("Set ARGUEBOT_APPIMAGE_RUNTIME to the checksum-verified runtime for the selected architecture.");
                    await PublisherProcess.RunAsync(Environment.GetEnvironmentVariable("ARGUEBOT_APPIMAGETOOL") ?? "appimagetool", new[] { "--no-appstream", "--runtime-file", runtimeFile, directory, artifact }, root, token, new Dictionary<string, string> { ["ARCH"] = runtime.EndsWith("arm64", StringComparison.Ordinal) ? "aarch64" : "x86_64", ["APPIMAGE_EXTRACT_AND_RUN"] = "1" }).ConfigureAwait(false);
                }
                else await PublisherProcess.RunAsync("fpm", new[] { "-s", "dir", "-t", channel, "-n", "arguebot", "-v", version, "--architecture", runtime.EndsWith("arm64", StringComparison.Ordinal) ? (channel == "rpm" ? "aarch64" : "arm64") : (channel == "rpm" ? "x86_64" : "amd64"), "--license", "MIT", "--description", "Moderated multi-agent debates with a shared web, terminal and CLI workspace", "--url", "https://github.com/jchristn/arguebot", "--maintainer", "ArgueBot maintainers", "--prefix", "/", "-C", directory, "-p", artifact, "." }, root, token).ConfigureAwait(false);
            }
            else throw new PlatformNotSupportedException("This native package requires its matching operating system.");
            if (!File.Exists(artifact)) throw new IOException("The native packager did not produce " + Path.GetFileName(artifact));
            string destination = Path.Combine(output, Path.GetFileName(artifact));
            File.Move(artifact, destination, true);
            return destination;
        }
        private static void Copy(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
            }
        }
    }
}
