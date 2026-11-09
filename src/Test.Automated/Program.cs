namespace ArgueBot.Tests.Automated
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    internal static class Program
    {
        private static Task<int> Main(string[] args)
        {
            return ConsoleRunner.RunMainAsync(AllSuites.All, args);
        }
    }
}
