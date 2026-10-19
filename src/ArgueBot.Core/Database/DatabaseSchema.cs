namespace ArgueBot.Core.Database
{
    /// <summary>Database compatibility independent of the application release version.</summary>
    public static class DatabaseSchema
    {
        /// <summary>Highest schema version this runtime can read and write.</summary>
        public const int CurrentVersion = 9;
    }
}
