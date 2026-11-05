namespace ArgueBot.Core.Database
{
    internal static class HarnessEnvironmentSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS connection_environment_owners(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, restricted INTEGER NOT NULL DEFAULT 1, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id));
CREATE TABLE IF NOT EXISTS connection_environment_variables(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, owner_id TEXT NOT NULL, position INTEGER NOT NULL, name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,owner_id) REFERENCES connection_environment_owners(tenant_id,id) ON DELETE CASCADE, UNIQUE(tenant_id,owner_id,name));
""";
    }
}
