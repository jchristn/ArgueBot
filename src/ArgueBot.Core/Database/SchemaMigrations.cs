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
                    new SchemaMigration { Version = 3, Description = "Structured request history with separate captured headers", Statements = new List<string> { SchemaDefinition.RequestHistory } },
                    new SchemaMigration { Version = 4, Description = "Global administrative recovery artifact inventory and operation lifecycle", Statements = new List<string> { SchemaDefinition.Backups } },
                    new SchemaMigration { Version = 5, Description = "Separate system administrator flags from tenant role names", Statements = new List<string> { "ALTER TABLE users ADD COLUMN is_admin INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE users ADD COLUMN is_tenant_admin INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE users ADD COLUMN protected INTEGER NOT NULL DEFAULT 0;", "UPDATE users SET is_tenant_admin=1 WHERE role='admin';", "UPDATE users SET is_admin=1,protected=1 WHERE id='usr_local' AND tenant_id='ten_local';" } }
                };
            }
        }
    }
}
