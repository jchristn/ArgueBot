namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Tenant role administration with protected seeds, revision checks and atomic audit events.</summary>
    public sealed class RoleMethods : IRoleMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal RoleMethods(DatabaseDriverBase database) { _Database = database; }
        /// <inheritdoc />
        public async Task<RolePage> EnumerateAsync(RequestContext context, string search = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            if (search == null || search.Length > 200 || pageNumber < 1 || pageSize < 1 || pageSize > 100) throw new DomainException(400, "invalid_filter", "Supply a bounded search and page size one through one hundred.");
            RolePage page = new RolePage { PageNumber = pageNumber, PageSize = pageSize };
            const string filter = "(tenant_id=@tenant OR tenant_id IS NULL) AND instr(lower(name),lower(@search))>0";
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM authorization_roles WHERE " + filter, token, P("tenant", context.TenantId, "search", search)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM authorization_roles WHERE " + filter + " ORDER BY name,id LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "search", search, "limit", pageSize, "offset", checked((long)(pageNumber - 1) * pageSize))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) page.Items.Add(Read(reader)); }
            }
            return page;
        }
        /// <inheritdoc />
        public async Task<RoleRecord> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            { return await ReadAsync(connection, null, context.TenantId, id, token).ConfigureAwait(false); }
        }
        /// <inheritdoc />
        public async Task<RoleRecord> CreateAsync(RequestContext context, RoleRequest request, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false); Validate(request);
            string id = Ids.New("rol_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                await CheckNameAsync(connection, transaction, context.TenantId, request.Name, id, token).ConfigureAwait(false);
                List<string> permissions = new List<string>();
                if (request.CloneRoleId.Length > 0)
                {
                    await ReadAsync(connection, transaction, context.TenantId, request.CloneRoleId, token).ConfigureAwait(false);
                    using (DbCommand command = Command(connection, transaction, "SELECT permission_id FROM authorization_role_permissions WHERE role_id=@role AND (tenant_id=@tenant OR tenant_id IS NULL) AND active=1 ORDER BY id LIMIT 129", P("role", request.CloneRoleId, "tenant", context.TenantId)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) permissions.Add(S(reader, "permission_id")); }
                    if (permissions.Count > 128) throw new DomainException(409, "role_mapping_limit", "Clone requires at most 128 active permission mappings.");
                }
                await ExecuteAsync(connection, transaction, "INSERT INTO authorization_roles(id,tenant_id,name,active,created_utc,updated_utc) VALUES(@id,@tenant,@name,@active,@now,@now)", token, P("id", id, "tenant", context.TenantId, "name", request.Name.Trim(), "active", request.Active ? 1 : 0)).ConfigureAwait(false);
                foreach (string permission in permissions)
                    await ExecuteAsync(connection, transaction, "INSERT INTO authorization_role_permissions(id,tenant_id,role_id,permission_id,created_utc,updated_utc) VALUES(@id,@tenant,@role,@permission,@now,@now)", token, P("id", Ids.New("rpm_"), "tenant", context.TenantId, "role", id, "permission", permission)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "role.create", "created", "Role", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public async Task<RoleRecord> UpdateAsync(RequestContext context, string id, RoleRequest request, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false); Validate(request);
            if (request.CloneRoleId.Length > 0) throw new DomainException(400, "invalid_role", "Cloning is available only when creating a role.");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                RoleRecord current = await ReadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false);
                if (current.Protected || current.Builtin || current.TenantId == null) throw new DomainException(403, "protected_role", "Clone a built-in before customizing it.");
                if (current.Revision != request.ExpectedRevision) throw new DomainException(409, "revision_conflict", "The role changed. Reload before saving.");
                await CheckNameAsync(connection, transaction, context.TenantId, request.Name, id, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE authorization_roles SET name=@name,active=@active,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", id, "name", request.Name.Trim(), "active", request.Active ? 1 : 0)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "role.update", "updated", "Role", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }
        private static void Validate(RoleRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.CloneRoleId == null || request.CloneRoleId.Length > 64 || request.ExpectedRevision < 0) throw new DomainException(400, "invalid_role", "Supply a bounded role name, optional clone identifier and nonnegative revision.");
        }
        private static async Task CheckNameAsync(DbConnection connection, DbTransaction transaction, string tenant, string name, string id, CancellationToken token)
        {
            if (BuiltInRoles.Find(name.Trim()) != null) throw new DomainException(409, "reserved_role_name", "Built-in names are reserved. Choose a custom name.");
            long count = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM authorization_roles WHERE tenant_id=@tenant AND lower(name)=lower(@name) AND id<>@id", token, P("tenant", tenant, "name", name.Trim(), "id", id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (count > 0) throw new DomainException(409, "role_name_conflict", "A role with that name already exists in this tenant.");
        }
        internal static async Task<RoleRecord> ReadAsync(DbConnection connection, DbTransaction? transaction, string tenant, string id, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM authorization_roles WHERE id=@id AND (tenant_id=@tenant OR tenant_id IS NULL)", P("id", id, "tenant", tenant)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Role not found."); return Read(reader); }
        }
        private static RoleRecord Read(DbDataReader reader)
        {
            return new RoleRecord { Id = S(reader, "id"), TenantId = N(reader, "tenant_id"), Name = S(reader, "name"), Builtin = Convert.ToInt32(reader["builtin"], CultureInfo.InvariantCulture) != 0, Protected = Convert.ToInt32(reader["protected"], CultureInfo.InvariantCulture) != 0, Active = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0, Revision = Convert.ToInt32(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
        }
    }
}
