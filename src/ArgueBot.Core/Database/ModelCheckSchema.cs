namespace ArgueBot.Core.Database
{
    internal static class ModelCheckSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS connection_model_checks(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, model_id TEXT NOT NULL, profile_id TEXT NOT NULL, command_key TEXT NOT NULL, check_tools INTEGER NOT NULL, state TEXT NOT NULL, completion_verified INTEGER NOT NULL DEFAULT 0, tools_verified INTEGER NOT NULL DEFAULT 0, error_code TEXT NOT NULL DEFAULT '', executable_version TEXT NOT NULL DEFAULT '', duration_ms BIGINT NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,model_id,command_key), FOREIGN KEY(tenant_id,profile_id) REFERENCES profiles(tenant_id,id));
CREATE INDEX IF NOT EXISTS ix_model_checks_history ON connection_model_checks(tenant_id,model_id,created_utc,id);
""";
    }
}
