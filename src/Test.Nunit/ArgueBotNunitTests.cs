namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>Runs each shared descriptor as its own NUnit test with native ignores and categories.</summary>
    public sealed class ArgueBotNunitTests
    {
        /// <summary>Every shared case.</summary>
        /// <returns>The cases.</returns>
        public static IEnumerable<TestCaseData> Cases() { return TouchstoneNunitCases.From(AllSuites.All); }

        /// <summary>Execute one shared case.</summary>
        /// <param name="testCase">The case to run.</param>
        /// <returns>A task that completes when the case has run.</returns>
        [TestCaseSource(nameof(Cases))]
        public Task Run(TestCaseDescriptor testCase) { return TouchstoneNunit.ExecuteAsync(testCase, null, null, TestContext.CurrentContext.CancellationToken); }
    }
}
