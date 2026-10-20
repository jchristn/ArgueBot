namespace ArgueBot.Core.Database
{
    internal static class AuthorizationSchema
    {
        internal const string Create = """
CREATE TABLE IF NOT EXISTS authorization_roles(id TEXT PRIMARY KEY, tenant_id TEXT REFERENCES tenants(id), name TEXT NOT NULL, builtin INTEGER NOT NULL DEFAULT 0, protected INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, revision INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS ix_roles_tenant_name ON authorization_roles(tenant_id,lower(name)) WHERE tenant_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ix_roles_builtin_name ON authorization_roles(lower(name)) WHERE tenant_id IS NULL;
CREATE TABLE IF NOT EXISTS authorization_permissions(id TEXT PRIMARY KEY, tenant_id TEXT REFERENCES tenants(id), name TEXT NOT NULL, resource_types TEXT NOT NULL, operations TEXT NOT NULL, effect TEXT NOT NULL, requires_ownership INTEGER NOT NULL DEFAULT 0, protected INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, revision INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS authorization_role_permissions(id TEXT PRIMARY KEY, tenant_id TEXT REFERENCES tenants(id), role_id TEXT NOT NULL REFERENCES authorization_roles(id) ON DELETE CASCADE, permission_id TEXT NOT NULL REFERENCES authorization_permissions(id), protected INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(role_id,permission_id));
CREATE TABLE IF NOT EXISTS authorization_user_assignments(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, user_id TEXT NOT NULL, role_id TEXT NOT NULL DEFAULT '', role_name TEXT NOT NULL DEFAULT '', system_kind TEXT NOT NULL DEFAULT '', resource_scope TEXT NOT NULL, resource_id TEXT NOT NULL DEFAULT '', inherits INTEGER NOT NULL DEFAULT 0, protected INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, revision INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,id));
CREATE UNIQUE INDEX IF NOT EXISTS ix_assignments_system_kind ON authorization_user_assignments(tenant_id,user_id,system_kind) WHERE system_kind<>'';
CREATE INDEX IF NOT EXISTS ix_assignments_principal ON authorization_user_assignments(tenant_id,user_id,active,id);
""";
    }
}
