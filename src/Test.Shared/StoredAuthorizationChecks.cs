namespace Test.Shared
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;

    internal static class StoredAuthorizationChecks
    {
        internal static async Task SqliteAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-stored-authority-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, ForeignKeys = true, Pooling = false }.ToString()))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    await RunAsync(database, connection, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        internal static async Task RunAsync(DatabaseDriverBase database, DbConnection connection, CancellationToken token)
        {
            string suffix = Guid.NewGuid().ToString("N");
            RequestContext owner = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
            UserRecord user = await database.Users.CreateAsync(owner, new UserRequest { Name = "Scoped fixture", Email = suffix + "@example.invalid", Role = "viewer" }, token).ConfigureAwait(false);
            RequestContext context = new RequestContext { TenantId = owner.TenantId, UserId = user.Id, IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, Role = "admin" };
            AuthorizationTarget target = new AuthorizationTarget { TenantId = owner.TenantId, ResourceType = "Conversation", Operation = "Read", ResourceId = "scope_" + suffix, OwnerUserId = user.Id };
            string viewer = Convert.ToString(await QueryAsync(connection, "SELECT id FROM authorization_roles WHERE tenant_id IS NULL AND name='Viewer'", token).ConfigureAwait(false), CultureInfo.InvariantCulture)!;
            string assignment = "asn_" + suffix;
            string denyRole = "rol_" + suffix; string denyPermission = "per_" + suffix; string denyAssignment = "den_" + suffix;
            try
            {
                await AssertAsync(database, context, target, false, "Caller flags bypassed current stored identity.", token).ConfigureAwait(false);
                await database.InitializeAsync(token).ConfigureAwait(false);
                if (Convert.ToString(await QueryAsync(connection, "SELECT id FROM authorization_roles WHERE tenant_id IS NULL AND name='Viewer'", token).ConfigureAwait(false), CultureInfo.InvariantCulture) != viewer) throw new InvalidDataException("Initialization replaced a built-in role identity.");
                if (Convert.ToInt64(await QueryAsync(connection, "SELECT COUNT(*) FROM authorization_roles WHERE tenant_id IS NULL AND builtin=1 AND protected=1", token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 8) throw new InvalidDataException("Built-in role catalog is incomplete or unprotected.");
                await ExecuteAsync(connection, "INSERT INTO authorization_user_assignments(id,tenant_id,user_id,role_id,role_name,resource_scope,resource_id,created_utc,updated_utc) VALUES(@id,'ten_local',@user,@role,'Viewer','Resource',@resource,@now,@now)", token, "id", assignment, "user", user.Id, "role", viewer, "resource", target.ResourceId).ConfigureAwait(false);
                await AssertAsync(database, context, target, true, "Stored scoped Viewer did not grant its resource.", token).ConfigureAwait(false);
                string resource = target.ResourceId; target.ResourceId = "other_" + suffix;
                await AssertAsync(database, context, target, false, "Resource assignment escaped its exact identifier.", token).ConfigureAwait(false); target.ResourceId = resource;
                target.Operation = "Update"; await AssertAsync(database, context, target, false, "Viewer granted a mutation.", token).ConfigureAwait(false); target.Operation = "Read";
                await ExecuteAsync(connection, "UPDATE authorization_roles SET active=0 WHERE tenant_id IS NULL AND id=@id", token, "id", viewer).ConfigureAwait(false);
                await AssertAsync(database, context, target, false, "Inactive GUID fell back to active built-in name.", token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_roles SET active=1 WHERE tenant_id IS NULL AND id=@id", token, "id", viewer).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_user_assignments SET role_id='missing_fixture_role' WHERE tenant_id='ten_local' AND id=@id", token, "id", assignment).ConfigureAwait(false);
                await AssertAsync(database, context, target, true, "Missing GUID failed to resolve the stored name.", token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_role_permissions SET active=0 WHERE tenant_id IS NULL AND role_id=@id", token, "id", viewer).ConfigureAwait(false);
                await AssertAsync(database, context, target, false, "Inactive permission mapping was bypassed by a baseline fallback.", token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_role_permissions SET active=1 WHERE tenant_id IS NULL AND role_id=@id", token, "id", viewer).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_permissions SET active=0 WHERE tenant_id IS NULL AND id IN (SELECT permission_id FROM authorization_role_permissions WHERE role_id=@id AND tenant_id IS NULL)", token, "id", viewer).ConfigureAwait(false);
                await AssertAsync(database, context, target, false, "Inactive permission still granted access.", token).ConfigureAwait(false);
                await database.InitializeAsync(token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_user_assignments SET active=0 WHERE tenant_id='ten_local' AND id=@id", token, "id", assignment).ConfigureAwait(false);
                await AssertAsync(database, context, target, false, "Inactive assignment remained cached.", token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_user_assignments SET active=1 WHERE tenant_id='ten_local' AND id=@id", token, "id", assignment).ConfigureAwait(false);
                await ExecuteAsync(connection, "INSERT INTO authorization_roles(id,tenant_id,name,created_utc,updated_utc) VALUES(@id,'ten_local',@name,@now,@now)", token, "id", denyRole, "name", "Deny fixture " + suffix).ConfigureAwait(false);
                await ExecuteAsync(connection, "INSERT INTO authorization_permissions(id,tenant_id,name,resource_types,operations,effect,created_utc,updated_utc) VALUES(@id,'ten_local','Deny read','[\"Conversation\"]','[\"Read\"]','Deny',@now,@now)", token, "id", denyPermission).ConfigureAwait(false);
                await ExecuteAsync(connection, "INSERT INTO authorization_role_permissions(id,tenant_id,role_id,permission_id,created_utc,updated_utc) VALUES(@id,'ten_local',@role,@permission,@now,@now)", token, "id", "map_" + suffix, "role", denyRole, "permission", denyPermission).ConfigureAwait(false);
                await ExecuteAsync(connection, "INSERT INTO authorization_user_assignments(id,tenant_id,user_id,role_id,resource_scope,resource_id,created_utc,updated_utc) VALUES(@id,'ten_local',@user,@role,'Resource',@resource,@now,@now)", token, "id", denyAssignment, "user", user.Id, "role", denyRole, "resource", target.ResourceId).ConfigureAwait(false);
                AuthorizationDecision denied = await database.Authorization.EvaluateUserAsync(context, target, token).ConfigureAwait(false);
                if (denied.Allowed || denied.Reason != "explicit_deny") throw new InvalidDataException("Stored deny did not override a permit.");
                await RoundTripAsync(database, context, target, denied.MatchedGrantIds[0], token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_user_assignments SET active=0 WHERE tenant_id='ten_local' AND id=@id", token, "id", denyAssignment).ConfigureAwait(false);
                await AssertAsync(database, context, target, true, "Inactive denial remained cached.", token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE users SET active=0 WHERE tenant_id='ten_local' AND id=@id", token, "id", user.Id).ConfigureAwait(false);
                await AssertAsync(database, context, target, false, "Disabled user retained stored authority.", token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE users SET active=1 WHERE tenant_id='ten_local' AND id=@id", token, "id", user.Id).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_user_assignments SET resource_scope='Tenant',resource_id='',inherits=0 WHERE tenant_id='ten_local' AND id=@id", token, "id", assignment).ConfigureAwait(false);
                await AssertAsync(database, context, target, false, "Non-inheriting tenant scope reached a child resource.", token).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE authorization_user_assignments SET inherits=1 WHERE tenant_id='ten_local' AND id=@id", token, "id", assignment).ConfigureAwait(false);
                await AssertAsync(database, context, target, true, "Inheriting tenant scope did not reach domain data.", token).ConfigureAwait(false);
                target.TenantId = "foreign_fixture_tenant";
                await AssertAsync(database, context, target, false, "Stored grants crossed the authenticated tenant.", token).ConfigureAwait(false); target.TenantId = owner.TenantId;
                // Corrupt only the disposable fixture to exercise recovery from a missing built-in record.
                await ExecuteAsync(connection, "DELETE FROM authorization_role_permissions WHERE role_id=@id AND tenant_id IS NULL", token, "id", viewer).ConfigureAwait(false);
                await ExecuteAsync(connection, "DELETE FROM authorization_roles WHERE id=@id AND tenant_id IS NULL", token, "id", viewer).ConfigureAwait(false);
                await AssertAsync(database, context, target, true, "Missing built-in record failed to use its baseline definition.", token).ConfigureAwait(false);
                await database.InitializeAsync(token).ConfigureAwait(false);
                await AssertAsync(database, context, target, true, "Recreated built-in broke a historical name assignment.", token).ConfigureAwait(false);
            }
            finally
            {
                await ExecuteAsync(connection, "DELETE FROM authorization_user_assignments WHERE tenant_id='ten_local' AND user_id=@id", CancellationToken.None, "id", user.Id).ConfigureAwait(false);
                await ExecuteAsync(connection, "DELETE FROM authorization_roles WHERE tenant_id='ten_local' AND id=@id", CancellationToken.None, "id", denyRole).ConfigureAwait(false);
                await ExecuteAsync(connection, "DELETE FROM authorization_permissions WHERE tenant_id='ten_local' AND id=@id", CancellationToken.None, "id", denyPermission).ConfigureAwait(false);
                await ExecuteAsync(connection, "UPDATE users SET active=0 WHERE tenant_id='ten_local' AND id=@id", CancellationToken.None, "id", user.Id).ConfigureAwait(false);
                await database.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static async Task RoundTripAsync(DatabaseDriverBase source, RequestContext context, AuthorizationTarget target, string matchedGrant, CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-authority-restore-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); paths.EnsureDirectories();
                string snapshot = Path.Combine(home, "authority.arguedb.jsonl");
                await source.ExportSnapshotAsync(snapshot, token).ConfigureAwait(false);
                using (SqliteDatabaseDriver restored = new SqliteDatabaseDriver(paths))
                {
                    await restored.InitializeAsync(token).ConfigureAwait(false);
                    await restored.RestoreSnapshotAsync(snapshot, token).ConfigureAwait(false);
                    await restored.InitializeAsync(token).ConfigureAwait(false);
                    AuthorizationDecision decision = await restored.Authorization.EvaluateUserAsync(context, target, token).ConfigureAwait(false);
                    if (decision.Allowed || decision.Reason != "explicit_deny" || decision.MatchedGrantIds.Count != 1 || decision.MatchedGrantIds[0] != matchedGrant) throw new InvalidDataException("Portable recovery changed stored authorization or grant provenance.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task AssertAsync(DatabaseDriverBase database, RequestContext context, AuthorizationTarget target, bool expected, string failure, CancellationToken token)
        { AuthorizationDecision result = await database.Authorization.EvaluateUserAsync(context, target, token).ConfigureAwait(false); if (result.Allowed != expected) throw new InvalidDataException(failure + " Decision: " + result.Reason); }
        private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken token, params object[] values)
        { using (DbCommand command = Command(connection, sql, values)) { await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); } }
        private static async Task<object?> QueryAsync(DbConnection connection, string sql, CancellationToken token)
        { using (DbCommand command = Command(connection, sql, Array.Empty<object>())) { return await command.ExecuteScalarAsync(token).ConfigureAwait(false); } }
        private static DbCommand Command(DbConnection connection, string sql, object[] values)
        {
            DbCommand command = connection.CreateCommand(); command.CommandText = sql;
            DbParameter now = command.CreateParameter(); now.ParameterName = "@now"; now.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); command.Parameters.Add(now);
            for (int index = 0; index < values.Length; index += 2) { DbParameter parameter = command.CreateParameter(); parameter.ParameterName = "@" + (string)values[index]; parameter.Value = values[index + 1]; command.Parameters.Add(parameter); }
            return command;
        }
    }
}
