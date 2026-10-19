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
#if NET10_0_OR_GREATER
                suites.AddRange(TerminalSuites.All);
#endif
                suites.AddRange(BackupSuites.All);
                suites.AddRange(ServiceLifecycleSuites.All);
                suites.AddRange(ConfigurationSuites.All);
                suites.AddRange(SecuritySuites.All); suites.AddRange(RunnerSuites.All); suites.AddRange(ProviderSuites.All); return suites;
            }
        }
    }
}
