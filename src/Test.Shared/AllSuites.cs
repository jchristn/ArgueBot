namespace Test.Shared
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>One authoritative suite inventory shared by every test adapter.</summary>
    public static class AllSuites
    {
        /// <summary>All descriptors, including explicitly skipped optional integrations.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>(StorageSuites.All);
                suites.AddRange(RunnerSuites.All); return suites;
            }
        }
    }
}
