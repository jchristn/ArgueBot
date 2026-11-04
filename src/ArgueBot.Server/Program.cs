namespace ArgueBot.Server
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Server.Services;
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--context-mcp")
            {
                try { await ArgueBot.Runners.Mcp.McpBridge.RunAsync(args[1], Console.OpenStandardInput(), Console.OpenStandardOutput()).ConfigureAwait(false); return 0; }
                catch (Exception failure) when (failure is not OutOfMemoryException) { return 2; }
            }
            string? home = null; int? port = null; bool detached = false;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--home" && index + 1 < args.Length) home = args[++index];
                else if (args[index] == "--port" && index + 1 < args.Length) port = int.Parse(args[++index]);
                else if (args[index] == "--detached") detached = true;
                else if (args[index] == "--help") { Console.WriteLine("ArgueBot.Server [--home PATH] [--port N]. Open workspace service."); return 0; }
                else { Console.Error.WriteLine("Unknown argument: " + args[index]); return 2; }
            }
            ArgueBotPaths paths = new ArgueBotPaths(home);
            StreamWriter? startupLog = null;
            if (detached)
            {
                NativeProcessSession.Detach();
                paths.EnsureDirectories();
                string logPath = Path.Combine(paths.Home, "logs", "startup.log");
                startupLog = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                HomePermissions.File(logPath);
                Console.SetOut(startupLog); Console.SetError(startupLog); Console.SetIn(TextReader.Null);
            }
            using (startupLog)
            using (CancellationTokenSource lifetime = new CancellationTokenSource())
            {
                using (PosixSignalRegistration? termination = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; lifetime.Cancel(); }))
                {
                    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
                    try
                    {
                        bool restart;
                        do
                        {
                            ServiceSettings settings = await ServiceSettings.LoadAsync(paths, lifetime.Token).ConfigureAwait(false);
                            if (port.HasValue) settings.Port = port.Value;
                            restart = await new ArgueBotServer().RunAsync(paths, settings, lifetime.Token).ConfigureAwait(false);
                            if (restart && Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true") return 0;
                        } while (restart && !lifetime.IsCancellationRequested);
                        return 0;
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return 130; }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine(ex.Message); return 1; }
                }
            }
        }
    }
}
