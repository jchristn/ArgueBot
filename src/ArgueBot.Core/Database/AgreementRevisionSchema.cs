namespace ArgueBot.Core.Database
{
    internal static class AgreementRevisionSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS debate_agreement_revisions(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, agreement_id TEXT NOT NULL, proposal_id TEXT NOT NULL, profile_id TEXT NOT NULL, message_id TEXT NOT NULL, position TEXT NOT NULL, rationale TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,proposal_id) REFERENCES debate_proposals(tenant_id,id) ON DELETE CASCADE, FOREIGN KEY(tenant_id,message_id) REFERENCES messages(tenant_id,id) ON DELETE CASCADE);
CREATE INDEX IF NOT EXISTS ix_debate_agreement_revisions_proposal ON debate_agreement_revisions(tenant_id,proposal_id,created_utc);
""";
    }
}
