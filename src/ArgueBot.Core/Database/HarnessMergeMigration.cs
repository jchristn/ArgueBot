namespace ArgueBot.Core.Database
{
    /// <summary>
    /// Folds reusable CLI harnesses into agents. Every model that ran on a harness becomes a CLI agent carrying the harness
    /// program, arguments, environment and secret plus the model name; agents that pointed at such a model are rewritten in
    /// place so their identifiers and role instructions survive. Frozen run snapshots are not changed.
    /// </summary>
    internal static class HarnessMergeMigration
    {
        internal const string Upgrade = """
INSERT INTO connection_models(id,tenant_id,name,endpoint_id,harness_id,model,context_characters,supports_tools,enabled,created_utc,updated_utc)
SELECT 'mod_'||h.id,h.tenant_id,h.name,'',h.id,'',262144,1,h.enabled,h.created_utc,h.updated_utc FROM connection_harnesses h
WHERE NOT EXISTS(SELECT 1 FROM connection_models m WHERE m.tenant_id=h.tenant_id AND m.harness_id=h.id) AND NOT EXISTS(SELECT 1 FROM connection_models x WHERE x.id='mod_'||h.id);
INSERT INTO profiles(id,tenant_id,user_id,name,kind,provider,endpoint,model,executable,cwd,secret_env,timeout_seconds,max_output_bytes,enabled,snapshot,created_utc,updated_utc)
SELECT 'par_'||m.id,m.tenant_id,COALESCE((SELECT MIN(u.id) FROM users u WHERE u.tenant_id=m.tenant_id),''),m.name,'cli',h.provider,'',m.model,h.executable,h.cwd,'',300,67108864,CASE WHEN m.enabled<>0 AND h.enabled<>0 THEN 1 ELSE 0 END,0,m.created_utc,m.updated_utc
FROM connection_models m JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id
WHERE NOT EXISTS(SELECT 1 FROM connection_profiles c JOIN profiles p ON p.id=c.id WHERE p.snapshot=0 AND c.tenant_id=m.tenant_id AND c.model_profile_id=m.id) AND NOT EXISTS(SELECT 1 FROM profiles x WHERE x.id='par_'||m.id);
INSERT INTO connection_profiles(id,tenant_id,model_profile_id,credential_id,role_instructions,context_characters,supports_tools,model_revision,transport_revision,transport_id,created_utc,updated_utc)
SELECT 'par_'||m.id,m.tenant_id,m.id,'','',m.context_characters,m.supports_tools,0,0,'',m.created_utc,m.updated_utc FROM connection_models m
WHERE m.harness_id<>'' AND EXISTS(SELECT 1 FROM profiles p WHERE p.id='par_'||m.id) AND NOT EXISTS(SELECT 1 FROM connection_profiles x WHERE x.id='par_'||m.id);
DELETE FROM profile_arguments WHERE profile_id IN (SELECT c.id FROM connection_profiles c JOIN profiles p ON p.id=c.id JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id WHERE p.snapshot=0 AND m.harness_id<>'');
INSERT INTO profile_arguments(id,tenant_id,profile_id,position,value,created_utc,updated_utc)
SELECT 'arg_'||a.id||'_'||c.id,c.tenant_id,c.id,a.position,a.value,a.created_utc,a.updated_utc
FROM connection_profiles c JOIN profiles p ON p.id=c.id JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id JOIN connection_arguments a ON a.tenant_id=m.tenant_id AND a.harness_id=m.harness_id
WHERE p.snapshot=0 AND m.harness_id<>'';
DELETE FROM connection_environment_variables WHERE owner_id IN (SELECT c.id FROM connection_profiles c JOIN profiles p ON p.id=c.id JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id WHERE p.snapshot=0 AND m.harness_id<>'');
DELETE FROM connection_environment_owners WHERE id IN (SELECT c.id FROM connection_profiles c JOIN profiles p ON p.id=c.id JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id WHERE p.snapshot=0 AND m.harness_id<>'');
INSERT INTO connection_environment_owners(id,tenant_id,restricted,created_utc,updated_utc)
SELECT c.id,c.tenant_id,1,c.created_utc,c.updated_utc FROM connection_profiles c JOIN profiles p ON p.id=c.id JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id
WHERE p.snapshot=0 AND m.harness_id<>'';
INSERT INTO connection_environment_variables(id,tenant_id,owner_id,position,name,created_utc,updated_utc)
SELECT 'env_'||v.id||'_'||c.id,c.tenant_id,c.id,v.position,v.name,v.created_utc,v.updated_utc
FROM connection_profiles c JOIN profiles p ON p.id=c.id JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id JOIN connection_environment_variables v ON v.tenant_id=m.tenant_id AND v.owner_id=m.harness_id
WHERE p.snapshot=0 AND m.harness_id<>'';
UPDATE profiles SET kind='cli',endpoint='',secret_env='',
provider=(SELECT h.provider FROM connection_profiles c JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id WHERE c.id=profiles.id),
executable=(SELECT h.executable FROM connection_profiles c JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id WHERE c.id=profiles.id),
cwd=(SELECT h.cwd FROM connection_profiles c JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id WHERE c.id=profiles.id),
model=(SELECT m.model FROM connection_profiles c JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id WHERE c.id=profiles.id),
max_output_bytes=COALESCE((SELECT g.output_bytes FROM connection_profiles c JOIN connection_model_generation g ON g.tenant_id=c.tenant_id AND g.id=c.model_profile_id WHERE c.id=profiles.id),max_output_bytes)
WHERE snapshot=0 AND id IN (SELECT c.id FROM connection_profiles c JOIN connection_models m ON m.tenant_id=c.tenant_id AND m.id=c.model_profile_id JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id);
UPDATE connection_profiles SET
credential_id=(SELECT h.credential_id FROM connection_models m JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id WHERE m.tenant_id=connection_profiles.tenant_id AND m.id=connection_profiles.model_profile_id),
context_characters=(SELECT m.context_characters FROM connection_models m WHERE m.tenant_id=connection_profiles.tenant_id AND m.id=connection_profiles.model_profile_id),
supports_tools=(SELECT m.supports_tools FROM connection_models m WHERE m.tenant_id=connection_profiles.tenant_id AND m.id=connection_profiles.model_profile_id),
model_profile_id='',model_revision=0,transport_revision=0,transport_id=''
WHERE id IN (SELECT p.id FROM profiles p WHERE p.snapshot=0) AND model_profile_id IN (SELECT m.id FROM connection_models m JOIN connection_harnesses h ON h.tenant_id=m.tenant_id AND h.id=m.harness_id);
DELETE FROM connection_model_checks WHERE model_id IN (SELECT id FROM connection_models WHERE harness_id<>'');
DELETE FROM connection_model_generation WHERE id IN (SELECT id FROM connection_models WHERE harness_id<>'');
DELETE FROM connection_models WHERE harness_id<>'';
DELETE FROM connection_environment_variables WHERE owner_id IN (SELECT id FROM connection_harnesses);
DELETE FROM connection_environment_owners WHERE id IN (SELECT id FROM connection_harnesses);
DROP TABLE IF EXISTS connection_arguments;
DROP TABLE IF EXISTS connection_harnesses;
""";
    }
}
