namespace ArgueBot.Core.Database
{
    /// <summary>Prompt library, per-conversation prompt selections and prompts frozen by each debate.</summary>
    internal static class PromptSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS prompt_templates(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, purpose TEXT NOT NULL, description TEXT NOT NULL, body TEXT NOT NULL, factory INTEGER NOT NULL DEFAULT 0, revision BIGINT NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_prompt_templates_tenant ON prompt_templates(tenant_id,purpose,name);
CREATE TABLE IF NOT EXISTS conversation_prompts(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, conversation_id TEXT NOT NULL, purpose TEXT NOT NULL, prompt_id TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,conversation_id,purpose));
CREATE TABLE IF NOT EXISTS run_prompts(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, run_id TEXT NOT NULL, purpose TEXT NOT NULL, prompt_id TEXT NOT NULL, prompt_revision BIGINT NOT NULL, name TEXT NOT NULL, body TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,run_id,purpose));
""";
    }
}
