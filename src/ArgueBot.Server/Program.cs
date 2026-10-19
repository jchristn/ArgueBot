namespace ArgueBot.Server
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string? home = null; int? port = null;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--home" && index + 1 < args.Length) home = args[++index];
                else if (args[index] == "--port" && index + 1 < args.Length) port = int.Parse(args[++index]);
                else if (args[index] == "--help") { Console.WriteLine("ArgueBot.Server [--home PATH] [--port N]. Local authenticated replacement service; legacy npm app is separate."); return 0; }
                else { Console.Error.WriteLine("Unknown argument: " + args[index]); return 2; }
            }
            using (CancellationTokenSource lifetime = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
                try
                {
                    ArgueBotPaths paths = new ArgueBotPaths(home);
                    ServiceSettings settings = await ServiceSettings.LoadAsync(paths, lifetime.Token).ConfigureAwait(false);
                    if (port.HasValue) settings.Port = port.Value;
                    await ArgueBotServer.RunAsync(paths, settings, lifetime.Token).ConfigureAwait(false); return 0;
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return 130; }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine(ex.Message); return 1; }
            }
        }
    }
}
