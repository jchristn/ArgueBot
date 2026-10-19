namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;

    internal static class PermissionChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, DbConnection connection, RequestContext owner, RequestContext subject, RequestContext foreign, CancellationToken token)
        {
            string suffix = Guid.NewGuid().ToString("N"); List<string> permissions = new List<string>();
            RoleRecord role = await database.Roles.CreateAsync(owner, new RoleRequest { Name = "Permission role " + suffix }, token).ConfigureAwait(false);
            PermissionRecord? foreignPermission = null;
            try
            {
                await DeniedAsync(async () => { await database.Permissions.EnumerateAsync(subject, token: token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Permissions.CreateAsync(owner, new PermissionRequest { Name = "Invalid operation", ResourceTypes = new List<string> { "Conversation" }, Operations = new List<string> { "Launch" } }, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Permissions.CreateAsync(owner, new PermissionRequest { Name = "Numeric effect", ResourceTypes = new List<string> { "Conversation" }, Operations = new List<string> { "Read" }, Effect = "0" }, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
                PermissionRecord permit = await database.Permissions.CreateAsync(owner, Request("Read %+議論 " + suffix, operations: "read"), token).ConfigureAwait(false); permissions.Add(permit.Id);
                if (permit.Operations.Single() != "Read") throw new InvalidDataException("Permission operations were not normalized to their public contract spelling.");
                PermissionRecord deny = await database.Permissions.CreateAsync(owner, Request("Deny " + suffix, effect: "Deny"), token).ConfigureAwait(false); permissions.Add(deny.Id);
                PermissionPage search = await database.Permissions.EnumerateAsync(owner, "%+議論 " + suffix, pageSize: 1, token: token).ConfigureAwait(false);
                if (search.TotalCount != 1 || search.Items.Count != 1 || search.Items[0].Id != permit.Id) throw new InvalidDataException("Permission paging/search lost literal Unicode metadata.");
                role = await database.RolePermissions.ReplaceAsync(owner, role.Id, new RolePermissionsRequest { PermissionIds = new List<string> { permit.Id }, ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                RolePermissionPage initial = await database.RolePermissions.EnumerateAsync(owner, role.Id, token: token).ConfigureAwait(false);
                if (initial.TotalCount != 1 || initial.RoleRevision != role.Revision || initial.Items[0].Permission.Id != permit.Id) throw new InvalidDataException("Role mapping pages lost permission metadata or role revision.");
                string mapping = initial.Items[0].Id;
                string resource = "scope_" + suffix;
                using (DbCommand command = Command(connection, "INSERT INTO authorization_user_assignments(id,tenant_id,user_id,role_id,resource_scope,resource_id,created_utc,updated_utc) VALUES(@id,@tenant,@user,@role,'Resource',@resource,@now,@now)", "id", "asn_" + suffix, "tenant", owner.TenantId, "user", subject.UserId, "role", role.Id, "resource", resource))
                { await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                AuthorizationTarget target = new AuthorizationTarget { TenantId = owner.TenantId, ResourceType = "Conversation", Operation = "Read", ResourceId = resource, OwnerUserId = subject.UserId };
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                role = await database.RolePermissions.ReplaceAsync(owner, role.Id, new RolePermissionsRequest { PermissionIds = new List<string> { permit.Id, deny.Id }, ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                AuthorizationDecision denied = await database.Authorization.EvaluateUserAsync(subject, target, token).ConfigureAwait(false);
                if (denied.Allowed || denied.Reason != "explicit_deny") throw new InvalidDataException("Managed explicit deny did not override the permit.");
                deny = await database.Permissions.UpdateAsync(owner, deny.Id, Request(deny.Name, deny.Revision, false, "Deny"), token).ConfigureAwait(false);
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                permit = await database.Permissions.UpdateAsync(owner, permit.Id, Request(permit.Name, permit.Revision, operations: "Write"), token).ConfigureAwait(false);
                await AssertAsync(database, subject, target, false, token).ConfigureAwait(false);
                target.Operation = "Write"; await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                target.Operation = "Execute"; await AssertAsync(database, subject, target, false, token).ConfigureAwait(false); target.Operation = "Update";
                await DeniedAsync(async () => { await database.Permissions.UpdateAsync(owner, permit.Id, Request(permit.Name, 1), token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                role = await database.RolePermissions.ReplaceAsync(owner, role.Id, new RolePermissionsRequest { PermissionIds = new List<string> { deny.Id }, ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                await AssertAsync(database, subject, target, false, token).ConfigureAwait(false);
                RolePermissionPage inactive = await database.RolePermissions.EnumerateAsync(owner, role.Id, active: false, token: token).ConfigureAwait(false);
                if (inactive.Items.Count != 1 || inactive.Items[0].Id != mapping) throw new InvalidDataException("Mapping replacement deleted historical identity.");
                role = await database.RolePermissions.ReplaceAsync(owner, role.Id, new RolePermissionsRequest { PermissionIds = new List<string> { permit.Id }, ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                RolePermissionPage current = await database.RolePermissions.EnumerateAsync(owner, role.Id, token: token).ConfigureAwait(false);
                if (current.Items[0].Id != mapping) throw new InvalidDataException("Reactivating a mapping replaced its identity.");
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.RolePermissions.ReplaceAsync(owner, role.Id, new RolePermissionsRequest { ExpectedRevision = 1 }, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.RolePermissions.ReplaceAsync(owner, role.Id, new RolePermissionsRequest { PermissionIds = new List<string> { permit.Id, permit.Id }, ExpectedRevision = role.Revision }, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
                foreignPermission = await database.Permissions.CreateAsync(foreign, Request("Foreign " + suffix), token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Permissions.ReadAsync(owner, foreignPermission.Id, token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.RolePermissions.ReplaceAsync(owner, role.Id, new RolePermissionsRequest { PermissionIds = new List<string> { foreignPermission.Id }, ExpectedRevision = role.Revision }, token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                if ((await database.Permissions.EnumerateAsync(owner, "Foreign " + suffix, token: token).ConfigureAwait(false)).TotalCount != 0) throw new InvalidDataException("Permission list leaked foreign counts.");
                if ((await database.Roles.ReadAsync(owner, role.Id, token).ConfigureAwait(false)).Revision != role.Revision) throw new InvalidDataException("A rejected mapping replacement advanced the role revision.");
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                RoleRecord builtin = (await database.Roles.EnumerateAsync(owner, "Viewer", token: token).ConfigureAwait(false)).Items.Single();
                RolePermissionRecord baseline = (await database.RolePermissions.EnumerateAsync(owner, builtin.Id, token: token).ConfigureAwait(false)).Items.Single();
                await DeniedAsync(async () => { await database.Permissions.UpdateAsync(owner, baseline.PermissionId, Request("Changed baseline", baseline.Permission.Revision), token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.RolePermissions.ReplaceAsync(owner, builtin.Id, new RolePermissionsRequest { ExpectedRevision = builtin.Revision }, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                using (DbCommand command = Command(connection, "SELECT COUNT(*) FROM audit_events WHERE tenant_id=@tenant AND resource_id=@id AND operation='permission.update'", "tenant", owner.TenantId, "id", permit.Id))
                { if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1) throw new InvalidDataException("Permission update audit did not remain atomic across a stale write."); }
            }
            finally
            {
                using (DbCommand command = Command(connection, "DELETE FROM authorization_user_assignments WHERE tenant_id=@tenant AND role_id=@role AND user_id=@user", "tenant", owner.TenantId, "role", role.Id, "user", subject.UserId)) { await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false); }
                RoleRecord current = await database.Roles.ReadAsync(owner, role.Id, CancellationToken.None).ConfigureAwait(false);
                await database.Roles.UpdateAsync(owner, role.Id, new RoleRequest { Name = current.Name, Active = false, ExpectedRevision = current.Revision }, CancellationToken.None).ConfigureAwait(false);
                foreach (string id in permissions)
                {
                    PermissionRecord permission = await database.Permissions.ReadAsync(owner, id, CancellationToken.None).ConfigureAwait(false);
                    if (permission.Active) await database.Permissions.UpdateAsync(owner, id, new PermissionRequest { Name = permission.Name, ResourceTypes = permission.ResourceTypes, Operations = permission.Operations, Effect = permission.Effect, RequiresOwnership = permission.RequiresOwnership, Active = false, ExpectedRevision = permission.Revision }, CancellationToken.None).ConfigureAwait(false);
                }
                if (foreignPermission != null) await database.Permissions.UpdateAsync(foreign, foreignPermission.Id, Request(foreignPermission.Name, foreignPermission.Revision, false), CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static PermissionRequest Request(string name, int revision = 0, bool active = true, string effect = "Permit", string operations = "Read")
        { return new PermissionRequest { Name = name, ResourceTypes = new List<string> { "Conversation" }, Operations = new List<string> { operations }, Effect = effect, Active = active, ExpectedRevision = revision }; }
        private static async Task AssertAsync(DatabaseDriverBase database, RequestContext subject, AuthorizationTarget target, bool allowed, CancellationToken token)
        { if ((await database.Authorization.EvaluateUserAsync(subject, target, token).ConfigureAwait(false)).Allowed != allowed) throw new InvalidDataException("Managed permissions did not affect current authority as expected."); }
        private static async Task DeniedAsync(Func<Task> action, int expected)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == expected) { return; }
            throw new InvalidDataException("Permission management did not enforce status " + expected + ".");
        }
        private static DbCommand Command(DbConnection connection, string sql, params object[] values)
        {
            DbCommand command = connection.CreateCommand(); command.CommandText = sql;
            DbParameter now = command.CreateParameter(); now.ParameterName = "@now"; now.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); command.Parameters.Add(now);
            for (int index = 0; index < values.Length; index += 2) { DbParameter parameter = command.CreateParameter(); parameter.ParameterName = "@" + (string)values[index]; parameter.Value = values[index + 1]; command.Parameters.Add(parameter); }
            return command;
        }
    }
}
