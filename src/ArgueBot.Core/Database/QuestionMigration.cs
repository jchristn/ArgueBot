namespace ArgueBot.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Schema 35 statements: one statement per column so the migration runner can skip columns that already exist.</summary>
    internal static class QuestionMigration
    {
        internal static List<string> Statements()
        {
            List<string> result = new List<string> { MemorySchema.LegacyClarifications, QuestionSchema.Create };
            result.AddRange(QuestionSchema.Columns.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(line => line.Length > 0));
            result.Add(QuestionSchema.MigrateClarifications);
            return result;
        }
    }
}
