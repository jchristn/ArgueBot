namespace ArgueBot.Core.Database
{
    /// <summary>Supported persistence providers.</summary>
    public enum DatabaseTypeEnum
    {
        /// <summary>Embedded local default.</summary>
        Sqlite = 0,
        /// <summary>PostgreSQL. Value 2 preserves existing numeric settings.</summary>
        Postgresql = 2
    }
}
