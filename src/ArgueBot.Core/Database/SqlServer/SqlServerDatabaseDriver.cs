namespace ArgueBot.Core.Database.SqlServer
{
    /// <summary>SqlServer relational driver using provider-specific SQL and the shared domain operations.</summary>
    public sealed class SqlServerDatabaseDriver : DatabaseDriverBase
    {
        /// <summary>Create without implicitly provisioning a database or changing credentials.</summary>
        public SqlServerDatabaseDriver(DatabaseSettings settings) : base(settings) { }
    }
}
