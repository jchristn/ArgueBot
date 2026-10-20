namespace ArgueBot.Tests.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;
    using Touchstone.Core;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string? path = null; string? suite = null;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--results" && index + 1 < args.Length) path = args[++index];
                else if (args[index] == "--suite" && index + 1 < args.Length) suite = args[++index];
                else { Console.Error.WriteLine("Usage: Test.Automated [--suite ID] [--results PATH]"); return 2; }
            }
            IReadOnlyList<TestSuiteDescriptor> suites = suite == null ? AllSuites.All : AllSuites.All.Where(item => string.Equals(item.SuiteId, suite, StringComparison.OrdinalIgnoreCase)).ToList();
            if (suites.Count == 0) { Console.Error.WriteLine("No test suite matches: " + suite); return 2; }
            return await ConsoleRunner.RunAsync(suites, resultsPath: path).ConfigureAwait(false);
        }
    }
}
