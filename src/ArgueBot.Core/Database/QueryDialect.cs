namespace ArgueBot.Core.Database
{
    using System.Data.Common;
    using Microsoft.Data.Sqlite;
    using Npgsql;

    internal static class QueryDialect
    {
        internal static string Translate(DbConnection connection, string sql)
        {
            return connection switch
            {
                SqliteConnection => sql,
                NpgsqlConnection => Postgresql.Queries.Statements.Translate(sql),
                _ => throw new System.NotSupportedException("Database SQL dialect is not registered.")
            };
        }
    }
}
