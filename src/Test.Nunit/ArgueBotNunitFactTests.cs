namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>Runs every shared descriptor in one test, with suite hooks, as the console runner does.</summary>
    public sealed class ArgueBotNunitFactTests : TouchstoneNunitFactBase
    {
        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites { get { return AllSuites.All; } }

        /// <summary>Execute all shared suites.</summary>
        /// <returns>A task that completes when every suite has run.</returns>
        [Test]
        public Task RunAll() { return RunAllAsync(TestContext.CurrentContext.CancellationToken); }
    }
}
