namespace ArgueBot.Core.Database.Postgresql
{
    /// <summary>Postgresql relational driver using provider-specific SQL and the shared domain operations.</summary>
    public sealed class PostgresqlDatabaseDriver : DatabaseDriverBase
    {
        /// <summary>Create without implicitly provisioning a database or changing credentials.</summary>
        public PostgresqlDatabaseDriver(DatabaseSettings settings) : base(settings) { }
    }
}
