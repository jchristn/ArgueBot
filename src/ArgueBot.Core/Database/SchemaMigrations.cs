namespace ArgueBot.Core.Database
{
    using System.Collections.Generic;

    internal static class SchemaMigrations
    {
        internal static IReadOnlyList<SchemaMigration> All
        {
            get
            {
                return new List<SchemaMigration>
                {
                    new SchemaMigration { Version = 1, Description = "Initial persistent conversations, identity, profiles, runs and events" },
                    new SchemaMigration { Version = 2, Description = "Portable message and event sequence column names", Statements = new List<string> { "ALTER TABLE messages RENAME COLUMN sequence TO sequence_number;", "ALTER TABLE events RENAME COLUMN sequence TO sequence_number;" } },
                    new SchemaMigration { Version = 3, Description = "Structured request history with separate captured headers", Statements = new List<string> { SchemaDefinition.RequestHistory } }
                };
            }
        }
    }
}
