namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Current stored user authority. Transport and worker integration must provide server-resolved targets.</summary>
    public sealed class AuthorizationMethods
    {
        /// <summary>Maximum active assignments per user. Excess input fails closed rather than truncating permissions.</summary>
        public const int MaximumAssignments = 256;
        private readonly DatabaseDriverBase _Database;
        internal AuthorizationMethods(DatabaseDriverBase database) { _Database = database; }

        /// <summary>Read current authenticated user and tenant facts plus active scoped grants. Context flags and compatibility role names never supply authority.</summary>
        public async Task<ResolvedAuthorization> ResolveUserAsync(RequestContext context, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            ResolvedAuthorization result = new ResolvedAuthorization { Principal = new AuthorizationPrincipal { Id = context.UserId, TenantId = context.TenantId, Kind = AuthorizationPrincipalKind.User, IsAuthenticated = true } };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                using (DbCommand command = Command(connection, transaction, "SELECT u.active AS user_active,t.active AS tenant_active,u.is_admin,u.is_tenant_admin FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE u.tenant_id=@tenant AND u.id=@user", P("tenant", context.TenantId, "user", context.UserId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) return result;
                    result.Principal.IsActive = Flag(reader, "user_active") && Flag(reader, "tenant_active");
                    result.Principal.IsAdmin = Flag(reader, "is_admin"); result.Principal.IsTenantAdmin = Flag(reader, "is_tenant_admin");
                }
                if (!result.Principal.IsActive || result.Principal.IsAdmin || result.Principal.IsTenantAdmin) return result;
                List<StoredAssignment> assignments = new List<StoredAssignment>();
                using (DbCommand command = Command(connection, transaction, "SELECT * FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user AND active=1 ORDER BY id LIMIT @limit", P("tenant", context.TenantId, "user", context.UserId, "limit", MaximumAssignments + 1)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (assignments.Count == MaximumAssignments) throw Invalid("assignment_limit_exceeded");
                        string scope = S(reader, "resource_scope");
                        if (scope is not ("Tenant" or "Resource") || !Enum.TryParse(scope, false, out AuthorizationScope parsed) || !Enum.IsDefined(parsed)) throw Invalid("invalid_assignment");
                        assignments.Add(new StoredAssignment { Id = S(reader, "id"), RoleId = S(reader, "role_id"), RoleName = S(reader, "role_name"), Scope = parsed, ResourceId = S(reader, "resource_id"), Inherits = Flag(reader, "inherits") });
                    }
                }
                foreach (StoredAssignment assignment in assignments)
                    await ExpandAsync(connection, transaction, context.TenantId, assignment, result.Grants, token).ConfigureAwait(false);
                transaction.Commit();
            }
            return result;
        }
        /// <summary>Evaluate a server-resolved operation against current stored user authority.</summary>
        public async Task<AuthorizationDecision> EvaluateUserAsync(RequestContext context, AuthorizationTarget target, CancellationToken token = default)
        {
            ResolvedAuthorization resolved = await ResolveUserAsync(context, token).ConfigureAwait(false);
            return AuthorizationEvaluator.Evaluate(resolved.Principal, target, resolved.Grants);
        }
        private static async Task ExpandAsync(DbConnection connection, DbTransaction transaction, string tenant, StoredAssignment assignment, List<AuthorizationGrant> grants, CancellationToken token)
        {
            ResolvedRoleReference? role = await RoleReferenceResolver.ResolveAsync(connection, transaction, tenant, assignment.RoleId, assignment.RoleName, token).ConfigureAwait(false);
            if (role == null || !role.Active) return;
            if (string.Equals(role.Name, "ResourceAdmin", StringComparison.OrdinalIgnoreCase) && assignment.Scope != AuthorizationScope.Resource) throw Invalid("invalid_assignment");
            if (role.BaselineOnly)
            {
                BuiltInRoleDefinition definition = BuiltInRoles.Find(role.Name)!;
                foreach (BuiltInPermissionDefinition permission in definition.Permissions)
                    Add(grants, assignment, tenant, "builtin:" + definition.Name + ":" + permission.Key, new List<string>(permission.ResourceTypes), new List<string>(permission.Operations), AuthorizationEffect.Permit, permission.RequiresOwnership);
                return;
            }
            using (DbCommand command = Command(connection, transaction, "SELECT m.id,p.resource_types,p.operations,p.effect,p.requires_ownership FROM authorization_role_permissions m JOIN authorization_permissions p ON p.id=m.permission_id WHERE m.role_id=@role AND (m.tenant_id=@tenant OR m.tenant_id IS NULL) AND (p.tenant_id=@tenant OR p.tenant_id IS NULL) AND m.active=1 AND p.active=1 ORDER BY m.id LIMIT @limit", P("role", role.Id, "tenant", tenant, "limit", AuthorizationEvaluator.MaximumGrants + 1)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    string effect = S(reader, "effect");
                    if (effect is not ("Permit" or "Deny") || !Enum.TryParse(effect, false, out AuthorizationEffect parsed) || !Enum.IsDefined(parsed)) throw Invalid("invalid_permission");
                    Add(grants, assignment, tenant, S(reader, "id"), Parse(S(reader, "resource_types")), Parse(S(reader, "operations")), parsed, Flag(reader, "requires_ownership"));
                }
            }
        }
        private static void Add(List<AuthorizationGrant> grants, StoredAssignment assignment, string tenant, string mapping, List<string> resources, List<string> operations, AuthorizationEffect effect, bool ownership)
        {
            if (grants.Count >= AuthorizationEvaluator.MaximumGrants) throw Invalid("grant_limit_exceeded");
            grants.Add(new AuthorizationGrant { Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assignment.Id + ":" + mapping))), TenantId = tenant, Scope = assignment.Scope, ResourceId = assignment.ResourceId, InheritsToChildren = assignment.Inherits, ResourceTypes = resources, Operations = operations, Effect = effect, RequiresOwnership = ownership });
        }
        private static List<string> Parse(string json)
        {
            if (json.Length > 8192) throw Invalid("invalid_permission");
            try { List<string>? values = JsonSerializer.Deserialize<List<string>>(json); if (values == null || values.Count == 0 || values.Count > 128) throw Invalid("invalid_permission"); return values; }
            catch (JsonException) { throw Invalid("invalid_permission"); }
        }
        private static bool Flag(DbDataReader reader, string column) { return Convert.ToInt32(reader[column], CultureInfo.InvariantCulture) != 0; }
        private static DomainException Invalid(string code) { return new DomainException(503, code, "Stored authorization cannot be resolved safely. Administrative repair is required."); }
    }
}
