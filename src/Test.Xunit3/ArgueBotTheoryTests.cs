namespace Test.Xunit3
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Xunit3Adapter;
    using global::Xunit;

    /// <summary>Runs each shared descriptor as its own theory row with native skips and traits.</summary>
    [Collection(ArgueBotCollection.Name)]
    public sealed class ArgueBotTheoryTests
    {
        /// <summary>Every shared case as a theory row.</summary>
        /// <returns>The rows.</returns>
        public static TheoryData<TouchstoneXunit3Case> Cases() { return TouchstoneXunit3Cases.From<AllSuites>(); }

        /// <summary>Execute one shared case.</summary>
        /// <param name="testCase">The case to run.</param>
        /// <returns>A task that completes when the case has run.</returns>
        [Theory]
        [MemberData(nameof(Cases))]
        public Task Run(TouchstoneXunit3Case testCase) { return TouchstoneXunit3.ExecuteAsync(testCase, TestContext.Current.CancellationToken); }
    }
}
