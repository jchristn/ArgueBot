namespace ArgueBot.Core.Database
{
    internal static class MemorySchema
    {
        /// <summary>Schema 14 clarification requests. Upgrades from before schema 35 create it so the schema 35 migration can move its rows into question requests and drop it; new databases never have it.</summary>
        internal const string LegacyClarifications = """
CREATE TABLE IF NOT EXISTS debate_clarifications(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, conversation_id TEXT NOT NULL, run_id TEXT NOT NULL, profile_id TEXT NOT NULL, call_id TEXT NOT NULL, question TEXT NOT NULL, blocking INTEGER NOT NULL, status TEXT NOT NULL DEFAULT 'open', answer_message_id TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(run_id,call_id), FOREIGN KEY(tenant_id,run_id) REFERENCES runs(tenant_id,id) ON DELETE CASCADE);
""";
        internal const string Create = """
CREATE TABLE IF NOT EXISTS debate_summaries(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, run_id TEXT NOT NULL, message_id TEXT NOT NULL, attempt_id TEXT NOT NULL, content TEXT NOT NULL, through_sequence BIGINT NOT NULL, context_epoch BIGINT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id), FOREIGN KEY(tenant_id,run_id) REFERENCES runs(tenant_id,id) ON DELETE CASCADE, FOREIGN KEY(tenant_id,message_id) REFERENCES messages(tenant_id,id) ON DELETE CASCADE, FOREIGN KEY(tenant_id,attempt_id) REFERENCES generation_attempts(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS debate_summary_sources(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, summary_id TEXT NOT NULL, message_id TEXT NOT NULL, digest TEXT NOT NULL, position INTEGER NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(summary_id,message_id), FOREIGN KEY(tenant_id,summary_id) REFERENCES debate_summaries(tenant_id,id) ON DELETE CASCADE, FOREIGN KEY(tenant_id,message_id) REFERENCES messages(tenant_id,id) ON DELETE CASCADE);
""";
    }
}
