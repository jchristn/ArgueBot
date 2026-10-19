namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
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

    /// <summary>Tenant-bound user role assignment lifecycle with current authority, scope checks and atomic accounting.</summary>
    public sealed class UserRoleAssignmentMethods : IUserRoleAssignmentMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal UserRoleAssignmentMethods(DatabaseDriverBase database) { _Database = database; }
        /// <inheritdoc />
        public async Task<UserRoleAssignmentPage> EnumerateAsync(RequestContext context, string userId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100) throw new DomainException(400, "invalid_filter", "Supply a positive page and page size one through one hundred.");
            UserRoleAssignmentPage page = new UserRoleAssignmentPage { PageNumber = pageNumber, PageSize = pageSize };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                await CheckUserAsync(connection, null, context.TenantId, userId, false, token).ConfigureAwait(false);
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user", token, P("tenant", context.TenantId, "user", userId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user ORDER BY id LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "user", userId, "limit", pageSize, "offset", checked((long)(pageNumber - 1) * pageSize))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) page.Items.Add(Read(reader)); }
            }
            return page;
        }
        /// <inheritdoc />
        public async Task<UserRoleAssignmentRecord> ReadAsync(RequestContext context, string userId, string id, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            { return await ReadAsync(connection, null, context.TenantId, userId, id, token).ConfigureAwait(false); }
        }
        /// <inheritdoc />
        public async Task<UserRoleAssignmentRecord> CreateAsync(RequestContext context, string userId, UserRoleAssignmentRequest request, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false); Validate(request);
            string id = Ids.New("asn_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                await CheckUserAsync(connection, transaction, context.TenantId, userId, true, token).ConfigureAwait(false);
                await CheckReferenceAsync(connection, transaction, context.TenantId, request, token).ConfigureAwait(false);
                await CheckLimitAsync(connection, transaction, context.TenantId, userId, id, request.Active, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO authorization_user_assignments(id,tenant_id,user_id,role_id,role_name,resource_scope,resource_id,inherits,active,created_utc,updated_utc) VALUES(@id,@tenant,@user,@role,@name,@scope,@resource,@inherits,@active,@now,@now)", token, Values(context.TenantId, userId, id, request)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "user.role_assignment_create", "created", "Assignment", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, userId, id, token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public async Task<UserRoleAssignmentRecord> UpdateAsync(RequestContext context, string userId, string id, UserRoleAssignmentRequest request, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false); Validate(request);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                await CheckUserAsync(connection, transaction, context.TenantId, userId, true, token).ConfigureAwait(false);
                UserRoleAssignmentRecord current = await ReadAsync(connection, transaction, context.TenantId, userId, id, token).ConfigureAwait(false);
                if (current.Protected) throw new DomainException(403, "protected_assignment", "Protected role assignments cannot be changed through normal administration.");
                if (current.Revision != request.ExpectedRevision) throw new DomainException(409, "revision_conflict", "The assignment changed. Reload before saving.");
                await CheckReferenceAsync(connection, transaction, context.TenantId, request, token).ConfigureAwait(false);
                await CheckLimitAsync(connection, transaction, context.TenantId, userId, id, request.Active, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE authorization_user_assignments SET role_id=@role,role_name=@name,resource_scope=@scope,resource_id=@resource,inherits=@inherits,active=@active,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND user_id=@user AND id=@id", token, Values(context.TenantId, userId, id, request)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "user.role_assignment_update", "updated", "Assignment", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, userId, id, token).ConfigureAwait(false);
        }
        private static void Validate(UserRoleAssignmentRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.RoleId == null || request.RoleId.Length > 64 || request.RoleId.Any(char.IsWhiteSpace) || request.RoleName == null || request.RoleName.Length > 200 || (request.RoleId.Length == 0 && string.IsNullOrWhiteSpace(request.RoleName)) || request.ResourceId == null || request.ResourceId.Length > 64 || request.ResourceId.Any(char.IsWhiteSpace) || request.ExpectedRevision < 0) throw new DomainException(400, "invalid_assignment", "Supply a bounded role GUID/name, scope identifier and nonnegative revision.");
            if (request.ResourceScope is not ("Tenant" or "Resource") || (request.ResourceScope == "Tenant" && request.ResourceId.Length != 0) || (request.ResourceScope == "Resource" && (request.ResourceId.Length == 0 || request.InheritsToChildren))) throw new DomainException(400, "invalid_assignment", "Tenant scope has no resource identifier; Resource scope requires one exact identifier and cannot inherit.");
        }
        private static async Task CheckReferenceAsync(DbConnection connection, DbTransaction transaction, string tenant, UserRoleAssignmentRequest request, CancellationToken token)
        {
            ResolvedRoleReference? role = await RoleReferenceResolver.ResolveAsync(connection, transaction, tenant, request.RoleId, request.RoleName.Trim(), token).ConfigureAwait(false);
            if (role == null) throw new DomainException(404, "not_found", "Role reference not found in this tenant or the protected baseline.");
            if (string.Equals(role.Name, "ResourceAdmin", StringComparison.OrdinalIgnoreCase) && request.ResourceScope != "Resource") throw new DomainException(400, "invalid_assignment", "ResourceAdmin requires an exact resource scope.");
            // Preserve the supplied GUID for historical fallback, but canonicalize the actual resolved name.
            request.RoleName = role.Name;
        }
        private static async Task CheckUserAsync(DbConnection connection, DbTransaction? transaction, string tenant, string user, bool mutation, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT protected FROM users WHERE tenant_id=@tenant AND id=@user", P("tenant", tenant, "user", user)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "User not found.");
                if (mutation && Convert.ToInt32(reader["protected"], CultureInfo.InvariantCulture) != 0) throw new DomainException(403, "protected_user", "Protected identity assignments cannot be changed through normal administration.");
            }
        }
        private static async Task CheckLimitAsync(DbConnection connection, DbTransaction transaction, string tenant, string user, string id, bool active, CancellationToken token)
        {
            if (!active) return;
            long count = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user AND active=1 AND id<>@id", token, P("tenant", tenant, "user", user, "id", id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (count >= AuthorizationMethods.MaximumAssignments) throw new DomainException(409, "assignment_limit_exceeded", "A user can have at most 256 active role assignments.");
        }
        private static Dictionary<string, object?> Values(string tenant, string user, string id, UserRoleAssignmentRequest request)
        { return P("tenant", tenant, "user", user, "id", id, "role", request.RoleId, "name", request.RoleName, "scope", request.ResourceScope, "resource", request.ResourceId, "inherits", request.InheritsToChildren ? 1 : 0, "active", request.Active ? 1 : 0); }
        private static async Task<UserRoleAssignmentRecord> ReadAsync(DbConnection connection, DbTransaction? transaction, string tenant, string user, string id, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user AND id=@id", P("tenant", tenant, "user", user, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Assignment not found."); return Read(reader); }
        }
        private static UserRoleAssignmentRecord Read(DbDataReader reader)
        {
            return new UserRoleAssignmentRecord { Id = S(reader, "id"), TenantId = S(reader, "tenant_id"), UserId = S(reader, "user_id"), RoleId = S(reader, "role_id"), RoleName = S(reader, "role_name"), ResourceScope = S(reader, "resource_scope"), ResourceId = S(reader, "resource_id"), InheritsToChildren = Convert.ToInt32(reader["inherits"], CultureInfo.InvariantCulture) != 0, Active = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0, Protected = Convert.ToInt32(reader["protected"], CultureInfo.InvariantCulture) != 0, Revision = Convert.ToInt32(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
        }
    }
}
