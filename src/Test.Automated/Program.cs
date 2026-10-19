namespace ArgueBot.Tests.Automated
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string? path = null;
            for (int index = 0; index < args.Length - 1; index++) if (args[index] == "--results") path = args[index + 1];
            return await ConsoleRunner.RunAsync(AllSuites.All, resultsPath: path).ConfigureAwait(false);
        }
    }
}
