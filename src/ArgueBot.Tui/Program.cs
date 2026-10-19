namespace ArgueBot.Tui
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (Console.IsInputRedirected || Console.IsOutputRedirected) { Console.Error.WriteLine("Interactive terminal required. Use ArgueBot.Cli for redirected input/output."); return 2; }
            string? home = null; string endpoint = "http://127.0.0.1:4317";
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--home" && index + 1 < args.Length) home = args[++index];
                else if (args[index] == "--server" && index + 1 < args.Length) endpoint = args[++index];
                else { Console.WriteLine("ArgueBot.Tui [--home PATH] [--server URL]. Start ArgueBot.Server first."); return args[index] == "--help" ? 0 : 2; }
            }
            using (CancellationTokenSource lifetime = new CancellationTokenSource())
            using (ArgueBotClient client = new ArgueBotClient(endpoint))
            {
                try
                {
                    string? session = Environment.GetEnvironmentVariable("ARGUEBOT_TOKEN");
                    if (!string.IsNullOrWhiteSpace(session)) client.SetToken(session);
                    else await client.PairLocalAsync(new ArgueBotPaths(home).Home, lifetime.Token).ConfigureAwait(false);
                    await new ConversationApp(client).RunAsync(lifetime.Token).ConfigureAwait(false); return 0;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine(ex.Message); return 1; }
            }
        }
    }
}
