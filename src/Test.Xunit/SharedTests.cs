namespace Test.Xunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;
    using global::Xunit;

    /// <summary>Runs the same descriptors as the console test runner.</summary>
    public sealed class SharedTests : TouchstoneFactBase
    {
        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites { get { return AllSuites.All; } }
        /// <summary>Execute all shared suites.</summary>
        [Fact]
        public async Task RunAllAsyncDescriptors() { await RunAllAsync(); }
    }
}
