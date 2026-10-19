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
                foreach (Match column in Regex.Matches(table.Groups[2].Value, @"(?:^|, )([a-z_]+) (TEXT|INTEGER)\b")) columns.Add(column.Groups[1].Value, column.Groups[2].Value == "INTEGER");
                tables.Add(table.Groups[1].Value, columns);
            }
            return tables;
        }
        internal static string Normalize(string table, string column)
        { return (table == "messages" || table == "events") && column == "sequence" ? "sequence_number" : column; }
    }
}
