namespace ArgueBot.Core.Database
{
    internal static class AttemptTimingSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS attempt_timing(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, endpoint_wait_ms BIGINT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,id) REFERENCES generation_attempts(tenant_id,id) ON DELETE CASCADE);
""";
    }
}
