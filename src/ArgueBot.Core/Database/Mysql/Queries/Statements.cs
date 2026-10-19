namespace ArgueBot.Core.Database.Mysql.Queries
{
    using System.Text.RegularExpressions;

    internal static class Statements
    {
        internal static string Translate(string sql)
        {
            if (sql.StartsWith("PRAGMA", System.StringComparison.Ordinal)) return "SELECT 1;";
            sql = sql.Replace("SELECT name FROM sqlite_master WHERE name='schema_migrations'", "SELECT table_name FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='schema_migrations'");
            sql = TypedSchema(sql.Replace("INSERT OR IGNORE", "INSERT IGNORE"), false).Replace("outcome TEXT", "outcome VARCHAR(200)");
            MatchCollection indexes = Regex.Matches(sql, @"CREATE INDEX IF NOT EXISTS (\w+) ON (\w+)\(([^;]+)\);");
            foreach (Match index in indexes)
            {
                string name = index.Groups[1].Value; string table = index.Groups[2].Value; string columns = index.Groups[3].Value;
                sql = sql.Replace(index.Value, "");
                sql = Regex.Replace(sql, @"(CREATE TABLE IF NOT EXISTS " + table + @"\([^;]+)\);", "$1, INDEX " + name + "(" + columns + "));");
            }
            return sql;
        }
        private static string TypedSchema(string sql, bool sqlServer)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(sql, @"\b(id|tenant_id|user_id|conversation_id|profile_id|run_id|entity_id|recipient_id|result_id|history_id) TEXT", "$1 VARCHAR(64)");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(created_utc|updated_utc|applied_utc|expires_utc) TEXT", "$1 VARCHAR(40)");
            text = text.Replace("token_hash TEXT", "token_hash VARCHAR(64)").Replace("command_key TEXT", "command_key VARCHAR(200)");
            if (sqlServer) text = text.Replace("VARCHAR(", "NVARCHAR(").Replace(" TEXT", " NVARCHAR(MAX)");
            return text;
        }
    }
}
