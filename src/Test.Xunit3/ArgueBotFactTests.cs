namespace Test.Xunit3
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.Xunit3Adapter;
    using global::Xunit;

    /// <summary>Runs every shared descriptor in one fact, with suite hooks, as the console runner does.</summary>
    [Collection(ArgueBotCollection.Name)]
    public sealed class ArgueBotFactTests : TouchstoneXunit3FactBase
    {
        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites { get { return AllSuites.All; } }

        /// <summary>Execute all shared suites.</summary>
        /// <returns>A task that completes when every suite has run.</returns>
        [Fact]
        public Task RunAll() { return RunAllAsync(TestContext.Current.CancellationToken); }
    }
}
