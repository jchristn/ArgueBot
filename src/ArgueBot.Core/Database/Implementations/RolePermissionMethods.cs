namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Protected role mapping administration. Replacement is atomic, tenant-bound and preserves inactive history.</summary>
    public sealed class RolePermissionMethods : IRolePermissionMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal RolePermissionMethods(DatabaseDriverBase database) { _Database = database; }
        /// <inheritdoc />
        public async Task<RolePermissionPage> EnumerateAsync(RequestContext context, string roleId, bool active = true, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100) throw new DomainException(400, "invalid_filter", "Supply a positive page and page size one through one hundred.");
            RolePermissionPage page = new RolePermissionPage { PageNumber = pageNumber, PageSize = pageSize };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                RoleRecord role = await RoleMethods.ReadAsync(connection, transaction, context.TenantId, roleId, token).ConfigureAwait(false); page.RoleRevision = role.Revision;
                const string scope = " FROM authorization_role_permissions m JOIN authorization_permissions p ON p.id=m.permission_id WHERE m.role_id=@role AND m.active=@active AND (m.tenant_id=@tenant OR m.tenant_id IS NULL) AND (p.tenant_id=@tenant OR p.tenant_id IS NULL)";
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*)" + scope, token, P("role", roleId, "tenant", context.TenantId, "active", active ? 1 : 0)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, transaction, "SELECT p.*,m.id AS mapping_id,m.active AS mapping_active,m.protected AS mapping_protected" + scope + " ORDER BY m.id LIMIT @limit OFFSET @offset", P("role", roleId, "tenant", context.TenantId, "active", active ? 1 : 0, "limit", pageSize, "offset", checked((long)(pageNumber - 1) * pageSize))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        page.Items.Add(new RolePermissionRecord { Id = S(reader, "mapping_id"), RoleId = roleId, PermissionId = S(reader, "id"), Active = Convert.ToInt32(reader["mapping_active"], CultureInfo.InvariantCulture) != 0, Protected = Convert.ToInt32(reader["mapping_protected"], CultureInfo.InvariantCulture) != 0, Permission = PermissionMethods.Read(reader) });
                }
                transaction.Commit();
            }
            return page;
        }
        /// <inheritdoc />
        public async Task<RoleRecord> ReplaceAsync(RequestContext context, string roleId, RolePermissionsRequest request, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(request);
            if (request.PermissionIds == null || request.PermissionIds.Count > 128 || request.PermissionIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 64) || request.PermissionIds.Distinct(StringComparer.Ordinal).Count() != request.PermissionIds.Count || request.ExpectedRevision < 0) throw new DomainException(400, "invalid_role_permissions", "Supply at most 128 distinct permission identifiers and a nonnegative role revision.");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                RoleRecord role = await RoleMethods.ReadAsync(connection, transaction, context.TenantId, roleId, token).ConfigureAwait(false);
                if (role.Protected || role.Builtin || role.TenantId == null) throw new DomainException(403, "protected_role", "Clone a built-in before customizing its permissions.");
                if (role.Revision != request.ExpectedRevision) throw new DomainException(409, "revision_conflict", "The role changed. Reload before replacing its permissions.");
                foreach (string permission in request.PermissionIds) await PermissionMethods.ReadAsync(connection, transaction, context.TenantId, permission, token).ConfigureAwait(false);
                using (DbCommand command = Command(connection, transaction, "SELECT permission_id,active FROM authorization_role_permissions WHERE tenant_id=@tenant AND role_id=@role AND protected=1 LIMIT 129", P("tenant", context.TenantId, "role", roleId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    int count = 0;
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (++count > 128) throw new DomainException(409, "role_mapping_limit", "Protected mapping inventory exceeds the supported role limit.");
                        bool selected = request.PermissionIds.Contains(S(reader, "permission_id"), StringComparer.Ordinal);
                        if (selected != (Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0)) throw new DomainException(403, "protected_mapping", "Protected permission mappings cannot be changed.");
                    }
                }
                await ExecuteAsync(connection, transaction, "UPDATE authorization_role_permissions SET active=0,updated_utc=@now WHERE tenant_id=@tenant AND role_id=@role AND active=1 AND protected=0", token, P("tenant", context.TenantId, "role", roleId)).ConfigureAwait(false);
                foreach (string permission in request.PermissionIds)
                {
                    await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO authorization_role_permissions(id,tenant_id,role_id,permission_id,created_utc,updated_utc) VALUES(@id,@tenant,@role,@permission,@now,@now)", token, P("id", Ids.New("rpm_"), "tenant", context.TenantId, "role", roleId, "permission", permission)).ConfigureAwait(false);
                    await ExecuteAsync(connection, transaction, "UPDATE authorization_role_permissions SET active=1,updated_utc=@now WHERE tenant_id=@tenant AND role_id=@role AND permission_id=@permission AND active=0 AND protected=0", token, P("tenant", context.TenantId, "role", roleId, "permission", permission)).ConfigureAwait(false);
                }
                await ExecuteAsync(connection, transaction, "UPDATE authorization_roles SET revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@role", token, P("tenant", context.TenantId, "role", roleId)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "role.permissions_update", "updated", "Role", roleId, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await _Database.Roles.ReadAsync(context, roleId, token).ConfigureAwait(false);
        }
    }
}
