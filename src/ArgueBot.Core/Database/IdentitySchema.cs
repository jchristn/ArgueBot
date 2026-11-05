namespace ArgueBot.Core.Database
{
    /// <summary>Bearer-token credentials, public conversation share links and identity indexes (schema 30; sealed tokens from 31).</summary>
    internal static class IdentitySchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS auth_credentials(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, user_id TEXT NOT NULL, name TEXT NOT NULL, token_sha256 TEXT NOT NULL UNIQUE, token_last4 TEXT NOT NULL, token_sealed TEXT NOT NULL DEFAULT '', auth_mode TEXT NOT NULL DEFAULT 'DirectHeader', active INTEGER NOT NULL DEFAULT 1, protected INTEGER NOT NULL DEFAULT 0, expires_utc TEXT, last_used_utc TEXT, revision INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_auth_credentials_user ON auth_credentials(tenant_id,user_id);
CREATE TABLE IF NOT EXISTS conversation_shares(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, conversation_id TEXT NOT NULL, created_by_user_id TEXT NOT NULL, token_sha256 TEXT NOT NULL UNIQUE, token_last4 TEXT NOT NULL, token_sealed TEXT NOT NULL DEFAULT '', include_tools INTEGER NOT NULL DEFAULT 0, expires_utc TEXT NOT NULL, revoked_utc TEXT, last_viewed_utc TEXT, view_count INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_conversation_shares_conversation ON conversation_shares(tenant_id,conversation_id);
CREATE UNIQUE INDEX IF NOT EXISTS ix_users_tenant_email ON users(tenant_id,lower(email)) WHERE email <> '';
""";
    }
}
