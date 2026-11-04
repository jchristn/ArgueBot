namespace ArgueBot.Core.Database
{
    internal static class GenerationSettingsSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS connection_model_generation(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, output_bytes INTEGER NOT NULL DEFAULT 67108864, output_tokens INTEGER, temperature TEXT, top_p TEXT, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,id) REFERENCES connection_models(tenant_id,id) ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS connection_profile_generation(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, output_tokens INTEGER, temperature TEXT, top_p TEXT, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,id) REFERENCES profiles(tenant_id,id) ON DELETE CASCADE);
""";
    }
}
