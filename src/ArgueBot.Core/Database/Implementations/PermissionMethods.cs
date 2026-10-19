namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Tenant permission definitions with protected seeds, optimistic updates and atomic accounting.</summary>
    public sealed class PermissionMethods : IPermissionMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal PermissionMethods(DatabaseDriverBase database) { _Database = database; }
        /// <inheritdoc />
        public async Task<PermissionPage> EnumerateAsync(RequestContext context, string search = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            if (search == null || search.Length > 200 || pageNumber < 1 || pageSize < 1 || pageSize > 100) throw new DomainException(400, "invalid_filter", "Supply a bounded search and page size one through one hundred.");
            PermissionPage page = new PermissionPage { PageNumber = pageNumber, PageSize = pageSize };
            const string filter = "(tenant_id=@tenant OR tenant_id IS NULL) AND instr(lower(name),lower(@search))>0";
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM authorization_permissions WHERE " + filter, token, P("tenant", context.TenantId, "search", search)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM authorization_permissions WHERE " + filter + " ORDER BY name,id LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "search", search, "limit", pageSize, "offset", checked((long)(pageNumber - 1) * pageSize))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) page.Items.Add(Read(reader)); }
            }
            return page;
        }
        /// <inheritdoc />
        public async Task<PermissionRecord> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            { return await ReadAsync(connection, null, context.TenantId, id, token).ConfigureAwait(false); }
        }
        /// <inheritdoc />
        public async Task<PermissionRecord> CreateAsync(RequestContext context, PermissionRequest request, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false); Validate(request);
            string id = Ids.New("per_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO authorization_permissions(id,tenant_id,name,resource_types,operations,effect,requires_ownership,active,created_utc,updated_utc) VALUES(@id,@tenant,@name,@resources,@operations,@effect,@ownership,@active,@now,@now)", token, Values(context, id, request)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "permission.create", "created", "Permission", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public async Task<PermissionRecord> UpdateAsync(RequestContext context, string id, PermissionRequest request, CancellationToken token = default)
        {
            await AuthorizationAdministration.RequireAsync(_Database, context, token).ConfigureAwait(false); Validate(request);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await AuthorizationAdministration.RequireAsync(connection, transaction, context, token).ConfigureAwait(false);
                PermissionRecord current = await ReadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false);
                if (current.Protected || current.TenantId == null) throw new DomainException(403, "protected_permission", "Protected global permission definitions cannot be modified.");
                if (current.Revision != request.ExpectedRevision) throw new DomainException(409, "revision_conflict", "The permission changed. Reload before saving.");
                await ExecuteAsync(connection, transaction, "UPDATE authorization_permissions SET name=@name,resource_types=@resources,operations=@operations,effect=@effect,requires_ownership=@ownership,active=@active,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, Values(context, id, request)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "permission.update", "updated", "Permission", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }
        private static void Validate(PermissionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.ExpectedRevision < 0 || request.Effect is not ("Permit" or "Deny") || !Enum.TryParse(request.Effect, false, out AuthorizationEffect effect)) throw new DomainException(400, "invalid_permission", "Supply a bounded name, Permit or Deny effect and nonnegative revision.");
            AuthorizationGrant grant = new AuthorizationGrant { Id = "validation", TenantId = "validation", Scope = AuthorizationScope.Tenant, InheritsToChildren = true, ResourceTypes = request.ResourceTypes, Operations = request.Operations, Effect = effect };
            if (!AuthorizationEvaluator.IsValidGrant(grant)) throw new DomainException(400, "invalid_permission", "Supply one through 32 resource names and one through 16 supported operations. All is an explicit wildcard.");
            request.Operations = request.Operations.ConvertAll(value => AuthorizationEvaluator.CanonicalOperation(value)!);
        }
        private static Dictionary<string, object?> Values(RequestContext context, string id, PermissionRequest request)
        { return P("id", id, "tenant", context.TenantId, "name", request.Name.Trim(), "resources", JsonSerializer.Serialize(request.ResourceTypes), "operations", JsonSerializer.Serialize(request.Operations), "effect", request.Effect, "ownership", request.RequiresOwnership ? 1 : 0, "active", request.Active ? 1 : 0); }
        internal static async Task<PermissionRecord> ReadAsync(DbConnection connection, DbTransaction? transaction, string tenant, string id, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM authorization_permissions WHERE id=@id AND (tenant_id=@tenant OR tenant_id IS NULL)", P("id", id, "tenant", tenant)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Permission not found."); return Read(reader); }
        }
        internal static PermissionRecord Read(DbDataReader reader)
        {
            try
            {
                return new PermissionRecord { Id = S(reader, "id"), TenantId = N(reader, "tenant_id"), Name = S(reader, "name"), ResourceTypes = JsonSerializer.Deserialize<List<string>>(S(reader, "resource_types")) ?? throw new JsonException(), Operations = JsonSerializer.Deserialize<List<string>>(S(reader, "operations")) ?? throw new JsonException(), Effect = S(reader, "effect"), RequiresOwnership = Convert.ToInt32(reader["requires_ownership"], CultureInfo.InvariantCulture) != 0, Active = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0, Protected = Convert.ToInt32(reader["protected"], CultureInfo.InvariantCulture) != 0, Revision = Convert.ToInt32(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
            }
            catch (JsonException) { throw new DomainException(503, "invalid_permission", "Stored permission metadata cannot be read safely."); }
        }
    }
}
