namespace ArgueBot.Runners.Processes
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    internal static class HarnessEnvironment
    {
        private static readonly string[] _PlatformNames = new[] { "PATH", "HOME", "USER", "USERNAME", "LOGNAME", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "SYSTEMROOT", "WINDIR", "COMSPEC", "PATHEXT", "TMP", "TEMP", "TMPDIR", "LANG", "LANGUAGE", "LC_ALL", "LC_CTYPE", "TZ", "TERM", "COLORTERM", "NO_COLOR", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "SYSTEMDRIVE", "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432", "COMMONPROGRAMFILES", "COMMONPROGRAMFILES(X86)", "COMMONPROGRAMW6432", "PROGRAMDATA", "ALLUSERSPROFILE", "PUBLIC", "OS", "COMPUTERNAME", "USERDOMAIN", "USERDNSDOMAIN", "LOGONSERVER", "PROCESSOR_ARCHITECTURE", "PROCESSOR_IDENTIFIER", "NUMBER_OF_PROCESSORS", "PSMODULEPATH", "ONEDRIVE", "ONEDRIVECOMMERCIAL", "ONEDRIVECONSUMER", "CLAUDE_CODE_GIT_BASH_PATH" };
        private static readonly string[] _SecretMarkers = new[] { "KEY", "TOKEN", "SECRET", "PASSWORD", "PASSWD", "CREDENTIAL", "AUTH", "COOKIE", "SESSION", "PRIVATE", "CERT_PASS" };
        /// <summary>True when an inherited variable name looks like it carries a credential, so its value is redacted from model output and diagnostics. Other selected values are ordinary configuration and stay visible.</summary>
        internal static bool IsSecretName(string name) { return _SecretMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)); }
        internal static void Apply(ProcessStartInfo start, RunnerProfile profile)
        {
            if (profile.RestrictEnvironment)
            {
                HashSet<string> permitted = new HashSet<string>(_PlatformNames, StringComparer.OrdinalIgnoreCase);
                HashSet<string> selected = new HashSet<string>(profile.EnvironmentVariables, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                if (profile.SecretEnvironmentVariable.Length > 0) selected.Add(profile.SecretEnvironmentVariable);
                foreach (string name in new List<string>(start.Environment.Keys)) if (!permitted.Contains(name) && !selected.Contains(name)) start.Environment.Remove(name);
            }
            foreach (KeyValuePair<string, string> variable in profile.RuntimeEnvironment) start.Environment[variable.Key] = variable.Value;
        }
    }
}
