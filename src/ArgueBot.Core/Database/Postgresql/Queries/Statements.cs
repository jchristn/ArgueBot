namespace ArgueBot.Core.Database.Postgresql.Queries
{
    internal static class Statements
    {
        internal static string Translate(string sql)
        {
            if (sql.StartsWith("PRAGMA", System.StringComparison.Ordinal)) return "SELECT 1;";
            sql = sql.Replace("SELECT name FROM sqlite_master WHERE name='schema_migrations'", "SELECT table_name FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='schema_migrations'");
            if (sql.StartsWith("INSERT OR IGNORE", System.StringComparison.Ordinal)) return sql.Replace("INSERT OR IGNORE", "INSERT") + " ON CONFLICT DO NOTHING";
            return TypedSchema(sql.Replace("instr(", "strpos("));
        }
        private static string TypedSchema(string sql)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(sql, @"\b(id|tenant_id|user_id|conversation_id|profile_id|run_id|entity_id|recipient_id|result_id|history_id|backup_id|operation_id|created_by_tenant_id|created_by_user_id) TEXT", "$1 VARCHAR(64)");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(created_utc|updated_utc|applied_utc|expires_utc) TEXT", "$1 VARCHAR(40)");
            text = text.Replace("token_hash TEXT", "token_hash VARCHAR(64)").Replace("command_key TEXT", "command_key VARCHAR(200)");
            return text;
        }
    }
}
