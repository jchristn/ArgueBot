namespace ArgueBot.Core.Database
{
    /// <summary>Supported persistence providers.</summary>
    public enum DatabaseTypeEnum
    {
        /// <summary>Embedded local default.</summary>
        Sqlite,
        /// <summary>MySQL or MariaDB.</summary>
        Mysql,
        /// <summary>PostgreSQL.</summary>
        Postgresql,
        /// <summary>Microsoft SQL Server.</summary>
        SqlServer
    }
}
