namespace ArgueBot.Core.Database
{
    internal static class CatalogSchema
    {
        internal static string Version14 { get { return Create.Replace("max_concurrent_invocations INTEGER NOT NULL DEFAULT 1, ", "").Replace("transport_id TEXT NOT NULL DEFAULT '', ", ""); } }
        internal const string Create = """
CREATE TABLE IF NOT EXISTS connection_endpoints(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, protocol TEXT NOT NULL, base_url TEXT NOT NULL, max_concurrent_invocations INTEGER NOT NULL DEFAULT 1, timeout_seconds INTEGER NOT NULL, enabled INTEGER NOT NULL, revision BIGINT NOT NULL DEFAULT 1, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id));
CREATE TABLE IF NOT EXISTS connection_harnesses(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, provider TEXT NOT NULL, executable TEXT NOT NULL, cwd TEXT NOT NULL, credential_id TEXT NOT NULL, enabled INTEGER NOT NULL, revision BIGINT NOT NULL DEFAULT 1, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id));
CREATE TABLE IF NOT EXISTS connection_arguments(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, harness_id TEXT NOT NULL, position INTEGER NOT NULL, value TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,harness_id) REFERENCES connection_harnesses(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS connection_models(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, endpoint_id TEXT NOT NULL, harness_id TEXT NOT NULL, model TEXT NOT NULL, context_characters INTEGER NOT NULL, supports_tools INTEGER NOT NULL, enabled INTEGER NOT NULL, revision BIGINT NOT NULL DEFAULT 1, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id));
CREATE TABLE IF NOT EXISTS connection_bindings(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, owner_id TEXT NOT NULL, position INTEGER NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL, value TEXT NOT NULL, credential_id TEXT NOT NULL, environment_variable TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_connection_bindings_owner ON connection_bindings(tenant_id,owner_id,position);
CREATE TABLE IF NOT EXISTS connection_profiles(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, model_profile_id TEXT NOT NULL, credential_id TEXT NOT NULL, role_instructions TEXT NOT NULL, context_characters INTEGER NOT NULL, supports_tools INTEGER NOT NULL, model_revision BIGINT NOT NULL, transport_revision BIGINT NOT NULL, transport_id TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,id) REFERENCES profiles(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS connection_credentials(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, kind TEXT NOT NULL, ciphertext TEXT NOT NULL, status TEXT NOT NULL, revision BIGINT NOT NULL DEFAULT 1, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id));
""";
    }
}
