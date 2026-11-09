namespace Test.Xunit3
{
    using global::Xunit;

    /// <summary>Keeps the run-all fact and per-case theory from running at the same time, since cases start local services and processes.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ArgueBotCollection
    {
        /// <summary>Collection name.</summary>
        public const string Name = "ArgueBot shared suites";
    }
}
