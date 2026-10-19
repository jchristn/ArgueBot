namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>Runs the same descriptors as the console test runner.</summary>
    public sealed class SharedTests : TouchstoneNunitBase
    {
        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites { get { return AllSuites.All; } }
        /// <summary>Execute all shared suites.</summary>
        [Test]
        public async Task RunAllAsyncDescriptors() { await RunAllAsync().ConfigureAwait(false); }
    }
}
