namespace ArgueBot.Core.Database
{
    /// <summary>Tenant resource library, its revision history, and the resources each generation attempt read or wrote.</summary>
    internal static class ResourceSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS resources(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, name_key TEXT NOT NULL, title TEXT NOT NULL, description TEXT NOT NULL, tags_json TEXT NOT NULL, body TEXT NOT NULL, characters BIGINT NOT NULL DEFAULT 0, bytes BIGINT NOT NULL DEFAULT 0, locked INTEGER NOT NULL DEFAULT 0, revision BIGINT NOT NULL DEFAULT 1, created_by_user_id TEXT NOT NULL, updated_by_kind TEXT NOT NULL, updated_by_id TEXT NOT NULL, updated_by_name TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,name_key), UNIQUE(tenant_id,id));
CREATE TABLE IF NOT EXISTS resource_revisions(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, resource_id TEXT NOT NULL, revision BIGINT NOT NULL, name TEXT NOT NULL, title TEXT NOT NULL, description TEXT NOT NULL, tags_json TEXT NOT NULL, body TEXT NOT NULL, characters BIGINT NOT NULL DEFAULT 0, locked INTEGER NOT NULL, change_kind TEXT NOT NULL, author_kind TEXT NOT NULL, author_id TEXT NOT NULL, author_name TEXT NOT NULL, run_id TEXT NOT NULL DEFAULT '', attempt_id TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(resource_id,revision), FOREIGN KEY(tenant_id,resource_id) REFERENCES resources(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS attempt_resources(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, run_id TEXT NOT NULL, attempt_id TEXT NOT NULL, resource_id TEXT NOT NULL, name TEXT NOT NULL, revision BIGINT NOT NULL, access TEXT NOT NULL, tool TEXT NOT NULL, bytes BIGINT NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,attempt_id) REFERENCES generation_attempts(tenant_id,id) ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ix_attempt_resources_attempt ON attempt_resources(tenant_id,attempt_id,created_utc);
CREATE INDEX IF NOT EXISTS ix_attempt_resources_run ON attempt_resources(tenant_id,run_id,access);
""";
    }
}
