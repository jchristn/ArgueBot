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
                    new SchemaMigration { Version = 5, Description = "Separate system administrator flags from tenant role names", Statements = new List<string> { "ALTER TABLE users ADD COLUMN is_admin INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE users ADD COLUMN is_tenant_admin INTEGER NOT NULL DEFAULT 0;", "ALTER TABLE users ADD COLUMN protected INTEGER NOT NULL DEFAULT 0;", "UPDATE users SET is_tenant_admin=1 WHERE role='admin';", "UPDATE users SET is_admin=1,protected=1 WHERE id='usr_local' AND tenant_id='ten_local';" } },
                    new SchemaMigration { Version = 6, Description = "Committed stream event offsets in UTF-16 code units", Statements = new List<string> { "ALTER TABLE events ADD COLUMN text_offset INTEGER NOT NULL DEFAULT 0;" } },
                    new SchemaMigration { Version = 7, Description = "Explicit recovery destination provider and retained publication staging", Statements = new List<string> { "ALTER TABLE backup_operations ADD COLUMN target_provider TEXT NOT NULL DEFAULT '';", "ALTER TABLE backup_operations ADD COLUMN target_connection_env TEXT NOT NULL DEFAULT '';", "ALTER TABLE backup_operations ADD COLUMN retained_staging_home TEXT NOT NULL DEFAULT '';" } },
                    new SchemaMigration { Version = 8, Description = "Structured append-only security accounting", Statements = new List<string> { "ALTER TABLE audit_events ADD COLUMN session_id TEXT NOT NULL DEFAULT '';", "ALTER TABLE audit_events ADD COLUMN auth_scheme TEXT NOT NULL DEFAULT '';", "ALTER TABLE audit_events ADD COLUMN correlation_id TEXT NOT NULL DEFAULT '';", "ALTER TABLE audit_events ADD COLUMN resource_type TEXT NOT NULL DEFAULT '';", "ALTER TABLE audit_events ADD COLUMN resource_id TEXT NOT NULL DEFAULT '';", "ALTER TABLE audit_events ADD COLUMN error_code TEXT NOT NULL DEFAULT '';" } },
                    new SchemaMigration { Version = 9, Description = "Atomic generation attempts, exact context provenance and frozen run questions", Statements = new List<string> { "ALTER TABLE runs ADD COLUMN question TEXT NOT NULL DEFAULT '';", "UPDATE runs SET question=(SELECT title FROM conversations WHERE conversations.id=runs.conversation_id AND conversations.tenant_id=runs.tenant_id);", SchemaDefinition.Attempts } }
                };
            }
        }
    }
}
