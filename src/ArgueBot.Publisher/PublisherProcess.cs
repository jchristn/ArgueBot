namespace ArgueBot.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class PublisherProcess
    {
        internal static async Task RunAsync(string program, IEnumerable<string> arguments, string directory, CancellationToken token, IReadOnlyDictionary<string, string>? environment = null)
        {
            ProcessStartInfo start = new ProcessStartInfo(program) { WorkingDirectory = directory, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            if (environment != null) foreach (KeyValuePair<string, string> item in environment) start.Environment[item.Key] = item.Value;
            using (Process process = Process.Start(start) ?? throw new IOException("Could not start " + program))
            using (CancellationTokenRegistration registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }))
            {
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException(program + " exited with code " + process.ExitCode + ". Review its diagnostic output above.");
            }
        }
    }
}
