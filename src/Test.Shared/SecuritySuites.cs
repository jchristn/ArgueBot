namespace Test.Shared
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>Transport security checks shared by every test adapter.</summary>
    public static class SecuritySuites
    {
        /// <summary>Local certificate trust checks use disposable homes and certificates.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    new TestSuiteDescriptor("Security", "Transport safeguards", new List<TestCaseDescriptor>
                    {
                        new TestCaseDescriptor("Security", "TlsTrust", "Local TLS pins bind certificate, home, endpoint, name and validity without trusting arbitrary certificates", TlsChecks.RunAsync)
                    })
                };
            }
        }
    }
}
