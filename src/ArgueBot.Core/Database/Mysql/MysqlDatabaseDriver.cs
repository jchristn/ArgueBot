namespace ArgueBot.Core.Database.Mysql
{
    /// <summary>Mysql relational driver using provider-specific SQL and the shared domain operations.</summary>
    public sealed class MysqlDatabaseDriver : DatabaseDriverBase
    {
        /// <summary>Create without implicitly provisioning a database or changing credentials.</summary>
        public MysqlDatabaseDriver(DatabaseSettings settings) : base(settings) { }
    }
}
