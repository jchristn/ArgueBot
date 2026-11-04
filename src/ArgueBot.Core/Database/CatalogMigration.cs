namespace ArgueBot.Core.Database
{
    /// <summary>Promotes supported legacy reusable profiles without changing frozen run snapshots.</summary>
    internal static class CatalogMigration
    {
        internal const string Upgrade = """
INSERT INTO connection_endpoints(id,tenant_id,name,protocol,base_url,timeout_seconds,enabled,created_utc,updated_utc)
SELECT 'end_'||p.id,p.tenant_id,p.name,p.provider,p.endpoint,p.timeout_seconds,p.enabled,p.created_utc,p.updated_utc FROM profiles p
WHERE p.snapshot=0 AND p.kind='rest' AND p.provider IN ('ollama','openai') AND NOT EXISTS(SELECT 1 FROM connection_endpoints e WHERE e.id='end_'||p.id);
INSERT INTO connection_bindings(id,tenant_id,owner_id,position,kind,name,value,credential_id,environment_variable,created_utc,updated_utc)
SELECT 'bind_'||p.id,p.tenant_id,'end_'||p.id,0,'bearer','Authorization','','',p.secret_env,p.created_utc,p.updated_utc FROM profiles p
WHERE p.snapshot=0 AND p.kind='rest' AND p.provider IN ('ollama','openai') AND p.secret_env<>'' AND NOT EXISTS(SELECT 1 FROM connection_bindings b WHERE b.id='bind_'||p.id);
INSERT INTO connection_harnesses(id,tenant_id,name,provider,executable,cwd,credential_id,enabled,created_utc,updated_utc)
SELECT 'har_'||p.id,p.tenant_id,p.name,p.provider,p.executable,p.cwd,'',p.enabled,p.created_utc,p.updated_utc FROM profiles p
WHERE p.snapshot=0 AND p.kind='cli' AND p.secret_env='' AND NOT EXISTS(SELECT 1 FROM connection_harnesses h WHERE h.id='har_'||p.id);
INSERT INTO connection_arguments(id,tenant_id,harness_id,position,value,created_utc,updated_utc)
SELECT 'arg_'||a.id,a.tenant_id,'har_'||a.profile_id,a.position,a.value,a.created_utc,a.updated_utc FROM profile_arguments a JOIN connection_harnesses h ON h.tenant_id=a.tenant_id AND h.id='har_'||a.profile_id
WHERE NOT EXISTS(SELECT 1 FROM connection_arguments x WHERE x.id='arg_'||a.id);
INSERT INTO connection_models(id,tenant_id,name,endpoint_id,harness_id,model,context_characters,supports_tools,enabled,created_utc,updated_utc)
SELECT 'mod_'||p.id,p.tenant_id,p.name,CASE WHEN p.kind='rest' THEN 'end_'||p.id ELSE '' END,CASE WHEN p.kind='cli' THEN 'har_'||p.id ELSE '' END,CASE WHEN p.kind='rest' THEN p.model ELSE '' END,262144,1,p.enabled,p.created_utc,p.updated_utc FROM profiles p
WHERE p.snapshot=0 AND ((p.kind='rest' AND p.provider IN ('ollama','openai')) OR (p.kind='cli' AND p.secret_env='')) AND NOT EXISTS(SELECT 1 FROM connection_models m WHERE m.id='mod_'||p.id);
INSERT INTO connection_profiles(id,tenant_id,model_profile_id,credential_id,role_instructions,context_characters,supports_tools,model_revision,transport_revision,created_utc,updated_utc)
SELECT p.id,p.tenant_id,'mod_'||p.id,'','',262144,1,1,1,p.created_utc,p.updated_utc FROM profiles p JOIN connection_models m ON m.tenant_id=p.tenant_id AND m.id='mod_'||p.id
WHERE NOT EXISTS(SELECT 1 FROM connection_profiles c WHERE c.id=p.id);
""";
    }
}
