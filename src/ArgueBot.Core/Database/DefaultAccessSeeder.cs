namespace ArgueBot.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal static class DefaultAccessSeeder
    {
        internal static async Task ApplyAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
        {
            string reader = await RoleIdAsync(connection, transaction, "OwnedReader", token).ConfigureAwait(false);
            string writer = await RoleIdAsync(connection, transaction, "OwnedOperator", token).ConfigureAwait(false);
            string member = await RoleIdAsync(connection, transaction, "TenantMember", token).ConfigureAwait(false);
            string cursor = "";
            while (true)
            {
                List<DefaultAccessUser> users = new List<DefaultAccessUser>();
                const string sql = "SELECT u.id,u.tenant_id,u.role FROM users u LEFT JOIN authorization_user_assignments a ON a.tenant_id=u.tenant_id AND a.user_id=u.id AND a.system_kind='DefaultAccess' LEFT JOIN authorization_user_assignments p ON p.tenant_id=u.tenant_id AND p.user_id=u.id AND p.system_kind='TenantPresence' WHERE u.id>@cursor AND (a.id IS NULL OR p.id IS NULL OR a.role_id<>CASE WHEN u.role IN ('operator','admin') THEN @writer ELSE @reader END OR p.role_id<>@member OR a.active<>1 OR a.protected<>1 OR a.resource_scope<>'Tenant' OR a.resource_id<>'' OR a.inherits<>1 OR p.active<>1 OR p.protected<>1 OR p.resource_scope<>'Tenant' OR p.resource_id<>'' OR p.inherits<>1) ORDER BY u.id LIMIT 100";
                using (DbCommand command = Command(connection, transaction, sql, P("cursor", cursor, "reader", reader, "writer", writer, "member", member)))
                using (DbDataReader result = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await result.ReadAsync(token).ConfigureAwait(false)) { cursor = S(result, "id"); users.Add(new DefaultAccessUser { UserId = cursor, TenantId = S(result, "tenant_id"), Role = S(result, "role") }); } }
                foreach (DefaultAccessUser user in users)
                    await ApplyUserAsync(connection, transaction, user.TenantId, user.UserId, user.Role, null, token).ConfigureAwait(false);
                if (users.Count < 100) return;
            }
        }
        internal static async Task ApplyUserAsync(DbConnection connection, DbTransaction transaction, string tenant, string user, string role, RequestContext? actor, CancellationToken token)
        {
            string policy = role is "operator" or "admin" ? "OwnedOperator" : "OwnedReader";
            await SyncAsync(connection, transaction, tenant, user, "DefaultAccess", policy, actor, token).ConfigureAwait(false);
            await SyncAsync(connection, transaction, tenant, user, "TenantPresence", "TenantMember", actor, token).ConfigureAwait(false);
        }
        private static async Task<string> RoleIdAsync(DbConnection connection, DbTransaction transaction, string name, CancellationToken token)
        {
            object? result = await ScalarAsync(connection, transaction, "SELECT id FROM authorization_roles WHERE tenant_id IS NULL AND name=@name AND builtin=1 AND protected=1 AND active=1", token, P("name", name)).ConfigureAwait(false);
            return result as string ?? throw new InvalidOperationException("Protected default role catalog is unavailable.");
        }
        private static async Task SyncAsync(DbConnection connection, DbTransaction transaction, string tenant, string user, string kind, string name, RequestContext? actor, CancellationToken token)
        {
            string role = await RoleIdAsync(connection, transaction, name, token).ConfigureAwait(false);
            object? existing = await ScalarAsync(connection, transaction, "SELECT id FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user AND system_kind=@kind", token, P("tenant", tenant, "user", user, "kind", kind)).ConfigureAwait(false);
            string id = existing as string ?? Ids.New("asn_"); int changed;
            if (existing == null)
                changed = await ExecuteAsync(connection, transaction, "INSERT INTO authorization_user_assignments(id,tenant_id,user_id,role_id,role_name,resource_scope,inherits,protected,system_kind,created_utc,updated_utc) VALUES(@id,@tenant,@user,@role,@name,'Tenant',1,1,@kind,@now,@now)", token, P("id", id, "tenant", tenant, "user", user, "role", role, "name", name, "kind", kind)).ConfigureAwait(false);
            else
                changed = await ExecuteAsync(connection, transaction, "UPDATE authorization_user_assignments SET role_id=@role,role_name=@name,resource_scope='Tenant',resource_id='',inherits=1,active=1,protected=1,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND user_id=@user AND id=@id AND (role_id<>@role OR role_name<>@name OR resource_scope<>'Tenant' OR resource_id<>'' OR inherits<>1 OR active<>1 OR protected<>1)", token, P("id", id, "tenant", tenant, "user", user, "role", role, "name", name)).ConfigureAwait(false);
            if (changed == 0) return;
            if (actor != null)
                await AuditMethods.AppendAsync(connection, transaction, actor, "user.default_assignment_sync", "updated", "Assignment", id, "", token).ConfigureAwait(false);
            else
                await ExecuteAsync(connection, transaction, "INSERT INTO audit_events(id,tenant_id,operation,outcome,auth_scheme,resource_type,resource_id,created_utc,updated_utc) VALUES(@id,@tenant,'authorization.default_assignment_seed','migrated','SystemMigration','Assignment',@assignment,@now,@now)", token, P("id", Ids.New("aud_"), "tenant", tenant, "assignment", id)).ConfigureAwait(false);
        }
    }
}
