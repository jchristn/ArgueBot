namespace ArgueBot.Core.Database
{
    internal static class SummarySectionSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS debate_summary_sections(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, summary_id TEXT NOT NULL, kind TEXT NOT NULL, content TEXT NOT NULL, position INTEGER NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(summary_id,kind), FOREIGN KEY(tenant_id,summary_id) REFERENCES debate_summaries(tenant_id,id) ON DELETE CASCADE);
""";
    }
}
