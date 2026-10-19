namespace ArgueBot.Core.Database.SqlServer.Queries
{
    using System;
    using System.Text.RegularExpressions;

    internal static class Statements
    {
        internal static string Translate(string sql)
        {
            if (sql.StartsWith("PRAGMA", StringComparison.Ordinal)) return "SELECT 1;";
            sql = sql.Replace("SELECT name FROM sqlite_master WHERE name='schema_migrations'", "SELECT name FROM sys.tables WHERE name='schema_migrations'");
            if (sql.StartsWith("INSERT OR IGNORE", StringComparison.Ordinal))
            {
                string table = Regex.Match(sql, @"INTO (\w+)").Groups[1].Value;
                string condition = table == "schema_migrations" ? "version=3" : table == "tenants" ? "id='ten_local'" : "id='usr_local'";
                sql = "IF NOT EXISTS (SELECT 1 FROM " + table + " WHERE " + condition + ") " + sql.Replace("INSERT OR IGNORE", "INSERT");
            }
            sql = sql.Replace("ALTER TABLE messages RENAME COLUMN sequence TO sequence_number;", "EXEC sp_rename 'messages.sequence', 'sequence_number', 'COLUMN';").Replace("ALTER TABLE events RENAME COLUMN sequence TO sequence_number;", "EXEC sp_rename 'events.sequence', 'sequence_number', 'COLUMN';");
            sql = TypedSchema(sql, true);
            sql = Regex.Replace(sql, @"CREATE TABLE IF NOT EXISTS (\w+)", "IF OBJECT_ID(N'dbo.$1', N'U') IS NULL CREATE TABLE $1");
            sql = Regex.Replace(sql, @"CREATE INDEX IF NOT EXISTS (\w+) ON (\w+)", "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='$1' AND object_id=OBJECT_ID('$2')) CREATE INDEX $1 ON $2");
            sql = Regex.Replace(sql, @"instr\(lower\((\w+)\),lower\(@search\)\)", "CHARINDEX(lower(@search),lower($1))");
            sql = sql.Replace("LIMIT @limit OFFSET @offset", "OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY").Replace("LIMIT 1000", "OFFSET 0 ROWS FETCH NEXT 1000 ROWS ONLY");
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
