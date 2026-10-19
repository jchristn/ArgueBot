namespace ArgueBot.Core.Backups
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using ArgueBot.Core.Database;

    internal static class BackupSchema
    {
        internal static Dictionary<string, Dictionary<string, bool>> Tables()
        {
            Dictionary<string, Dictionary<string, bool>> tables = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
            foreach (Match table in Regex.Matches(SchemaDefinition.Create, @"CREATE TABLE IF NOT EXISTS (\w+)\(([^\n]+)"))
            {
                Dictionary<string, bool> columns = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (Match column in Regex.Matches(table.Groups[2].Value, @"(?:^|, )([a-z_]+) (TEXT|INTEGER|BIGINT)\b")) columns.Add(column.Groups[1].Value, column.Groups[2].Value != "TEXT");
                tables.Add(table.Groups[1].Value, columns);
            }
            return tables;
        }
        internal static Dictionary<string, bool> ColumnsAtVersion(string table, int version)
        {
            Dictionary<string, bool> columns = Tables()[table];
            if (table == "audit_events" && version < 8) { columns.Remove("session_id"); columns.Remove("auth_scheme"); columns.Remove("correlation_id"); columns.Remove("resource_type"); columns.Remove("resource_id"); columns.Remove("error_code"); }
            if (table == "users" && version < 5) { columns.Remove("is_admin"); columns.Remove("is_tenant_admin"); columns.Remove("protected"); }
            if (table == "backup_operations" && version < 7) { columns.Remove("target_provider"); columns.Remove("target_connection_env"); columns.Remove("retained_staging_home"); }
            if (table == "events" && version < 6) columns.Remove("text_offset");
            return columns;
        }
        internal static int IntroducedVersion(string table)
        { return table.StartsWith("backup_", StringComparison.Ordinal) ? 4 : table.StartsWith("request_history", StringComparison.Ordinal) ? 3 : 1; }
        internal static string Normalize(string table, string column)
        { return (table == "messages" || table == "events") && column == "sequence" ? "sequence_number" : column; }
    }
}
