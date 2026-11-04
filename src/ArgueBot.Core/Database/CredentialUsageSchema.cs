namespace ArgueBot.Core.Database
{
    internal static class CredentialUsageSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS attempt_credentials(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, attempt_id TEXT NOT NULL, credential_id TEXT NOT NULL, revision BIGINT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(attempt_id,credential_id,revision), FOREIGN KEY(tenant_id,attempt_id) REFERENCES generation_attempts(tenant_id,id) ON DELETE CASCADE);
""";
    }
}
