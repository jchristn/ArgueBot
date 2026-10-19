namespace Test.Shared
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;

    internal static class UserAssignmentChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, DbConnection connection, RequestContext owner, RequestContext subject, RequestContext foreign, string foreignRoleId, CancellationToken token)
        {
            string suffix = Guid.NewGuid().ToString("N");
            string resource = "scope_" + suffix;
            UserRoleAssignmentRecord? assignment = null; UserRoleAssignmentRecord? foreignAssignment = null; RoleRecord? custom = null;
            AuthorizationTarget target = new AuthorizationTarget { TenantId = owner.TenantId, ResourceType = "Conversation", Operation = "Read", ResourceId = resource, OwnerUserId = subject.UserId };
            try
            {
                await DeniedAsync(async () => { await database.UserRoleAssignments.EnumerateAsync(subject, subject.UserId, token: token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.CreateAsync(owner, owner.UserId, Request(resource), token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.CreateAsync(owner, subject.UserId, new UserRoleAssignmentRequest { RoleName = "Viewer", ResourceScope = "Resource" }, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.CreateAsync(owner, subject.UserId, new UserRoleAssignmentRequest { RoleName = "Viewer", ResourceScope = "Resource", ResourceId = resource, InheritsToChildren = true }, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.CreateAsync(owner, subject.UserId, new UserRoleAssignmentRequest { RoleName = "ResourceAdmin", ResourceScope = "Tenant", InheritsToChildren = true }, token).ConfigureAwait(false); }, 400).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.CreateAsync(owner, subject.UserId, new UserRoleAssignmentRequest { RoleId = foreignRoleId }, token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                assignment = await database.UserRoleAssignments.CreateAsync(owner, subject.UserId, Request(resource), token).ConfigureAwait(false);
                if (assignment.UserId != subject.UserId || assignment.TenantId != owner.TenantId || assignment.RoleName != "Viewer" || assignment.Protected || assignment.Revision != 1) throw new InvalidDataException("Assignment creation lost its current tenant/user/name binding.");
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                target.ResourceId = "other_" + suffix; await AssertAsync(database, subject, target, false, token).ConfigureAwait(false); target.ResourceId = resource;
                UserRoleAssignmentRequest update = new UserRoleAssignmentRequest { RoleName = "Viewer", ExpectedRevision = assignment.Revision };
                assignment = await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, update, token).ConfigureAwait(false);
                await AssertAsync(database, subject, target, false, token).ConfigureAwait(false);
                update.InheritsToChildren = true; update.ExpectedRevision = assignment.Revision;
                assignment = await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, update, token).ConfigureAwait(false);
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                RoleRecord builtin = (await database.Roles.EnumerateAsync(owner, "Viewer", token: token).ConfigureAwait(false)).Items[0];
                custom = await database.Roles.CreateAsync(owner, new RoleRequest { Name = "Inactive assignment role " + suffix, CloneRoleId = builtin.Id, Active = false }, token).ConfigureAwait(false);
                update.RoleId = custom.Id; update.RoleName = "Viewer"; update.ExpectedRevision = assignment.Revision;
                assignment = await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, update, token).ConfigureAwait(false);
                if (assignment.RoleName != custom.Name) throw new InvalidDataException("An existing role GUID did not dominate the fallback name.");
                await AssertAsync(database, subject, target, false, token).ConfigureAwait(false);
                custom = await database.Roles.UpdateAsync(owner, custom.Id, new RoleRequest { Name = custom.Name, Active = true, ExpectedRevision = custom.Revision }, token).ConfigureAwait(false);
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                update.RoleId = "missing_fixture_role"; update.RoleName = "viewer"; update.ExpectedRevision = assignment.Revision;
                assignment = await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, update, token).ConfigureAwait(false);
                if (assignment.RoleName != "Viewer") throw new InvalidDataException("Missing GUID/name fallback did not canonicalize the built-in reference.");
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, Request(resource), token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                await AssertAsync(database, subject, target, true, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.ReadAsync(owner, foreign.UserId, assignment.Id, token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.EnumerateAsync(owner, foreign.UserId, token: token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                foreignAssignment = await database.UserRoleAssignments.CreateAsync(foreign, foreign.UserId, Request(resource), token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.ReadAsync(owner, subject.UserId, foreignAssignment.Id, token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                UserRoleAssignmentPage page = await database.UserRoleAssignments.EnumerateAsync(owner, subject.UserId, pageSize: 1, token: token).ConfigureAwait(false);
                if (page.TotalCount != 1 || page.Items.Count != 1 || page.Items[0].Id != assignment.Id) throw new InvalidDataException("Assignment paging crossed principal or tenant boundaries.");
                using (DbCommand command = Command(connection, "UPDATE authorization_user_assignments SET protected=1 WHERE tenant_id=@tenant AND user_id=@user AND id=@id", "tenant", owner.TenantId, "user", subject.UserId, "id", assignment.Id)) { await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                try
                {
                    await DeniedAsync(async () => { await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, new UserRoleAssignmentRequest { RoleName = "Viewer", Active = false, ExpectedRevision = assignment.Revision }, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                }
                finally
                { using (DbCommand command = Command(connection, "UPDATE authorization_user_assignments SET protected=0 WHERE tenant_id=@tenant AND user_id=@user AND id=@id", "tenant", owner.TenantId, "user", subject.UserId, "id", assignment.Id)) { await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false); } }
                update.Active = false; update.ExpectedRevision = assignment.Revision;
                assignment = await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, update, token).ConfigureAwait(false);
                await AssertAsync(database, subject, target, false, token).ConfigureAwait(false);
                await LimitsAsync(database, connection, owner, subject, token).ConfigureAwait(false);
            }
            finally
            {
                if (assignment != null && assignment.Active) await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, assignment.Id, new UserRoleAssignmentRequest { RoleId = assignment.RoleId, RoleName = assignment.RoleName, ResourceScope = assignment.ResourceScope, ResourceId = assignment.ResourceId, InheritsToChildren = assignment.InheritsToChildren, Active = false, ExpectedRevision = assignment.Revision }, CancellationToken.None).ConfigureAwait(false);
                if (foreignAssignment != null) await database.UserRoleAssignments.UpdateAsync(foreign, foreign.UserId, foreignAssignment.Id, new UserRoleAssignmentRequest { RoleName = "Viewer", ResourceScope = "Resource", ResourceId = resource, Active = false, ExpectedRevision = foreignAssignment.Revision }, CancellationToken.None).ConfigureAwait(false);
                if (custom != null && custom.Active) await database.Roles.UpdateAsync(owner, custom.Id, new RoleRequest { Name = custom.Name, Active = false, ExpectedRevision = custom.Revision }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static async Task LimitsAsync(DatabaseDriverBase database, DbConnection connection, RequestContext owner, RequestContext subject, CancellationToken token)
        {
            string suffix = Guid.NewGuid().ToString("N");
            RoleRecord role = await database.Roles.CreateAsync(owner, new RoleRequest { Name = "Assignment limit " + suffix }, token).ConfigureAwait(false);
            string first = "limit_" + suffix + "_0";
            try
            {
                // Populate only disposable fixture metadata to exercise the admission boundary without hundreds of HTTP exchanges.
                for (int index = 0; index < 256; index++)
                {
                    using (DbCommand command = Command(connection, "INSERT INTO authorization_user_assignments(id,tenant_id,user_id,role_id,resource_scope,created_utc,updated_utc) VALUES(@id,@tenant,@user,@role,'Tenant',@now,@now)", "id", "limit_" + suffix + "_" + index, "tenant", owner.TenantId, "user", subject.UserId, "role", role.Id))
                    { await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                }
                await DeniedAsync(async () => { await database.UserRoleAssignments.CreateAsync(owner, subject.UserId, new UserRoleAssignmentRequest { RoleId = role.Id }, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                UserRoleAssignmentRecord inactive = await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, first, new UserRoleAssignmentRequest { RoleId = role.Id, Active = false, ExpectedRevision = 1 }, token).ConfigureAwait(false);
                await database.UserRoleAssignments.CreateAsync(owner, subject.UserId, new UserRoleAssignmentRequest { RoleId = role.Id }, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.UserRoleAssignments.UpdateAsync(owner, subject.UserId, first, new UserRoleAssignmentRequest { RoleId = role.Id, Active = true, ExpectedRevision = inactive.Revision }, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
            }
            finally
            {
                using (DbCommand command = Command(connection, "DELETE FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user AND role_id=@role", "tenant", owner.TenantId, "user", subject.UserId, "role", role.Id)) { await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false); }
                await database.Roles.UpdateAsync(owner, role.Id, new RoleRequest { Name = role.Name, Active = false, ExpectedRevision = role.Revision }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static DbCommand Command(DbConnection connection, string sql, params object[] values)
        {
            DbCommand command = connection.CreateCommand(); command.CommandText = sql;
            DbParameter now = command.CreateParameter(); now.ParameterName = "@now"; now.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); command.Parameters.Add(now);
            for (int index = 0; index < values.Length; index += 2) { DbParameter parameter = command.CreateParameter(); parameter.ParameterName = "@" + (string)values[index]; parameter.Value = values[index + 1]; command.Parameters.Add(parameter); }
            return command;
        }
        private static UserRoleAssignmentRequest Request(string resource)
        { return new UserRoleAssignmentRequest { RoleName = "Viewer", ResourceScope = "Resource", ResourceId = resource }; }
        private static async Task AssertAsync(DatabaseDriverBase database, RequestContext subject, AuthorizationTarget target, bool expected, CancellationToken token)
        { if ((await database.Authorization.EvaluateUserAsync(subject, target, token).ConfigureAwait(false)).Allowed != expected) throw new InvalidDataException("Managed scoped assignment did not affect stored authority as expected."); }
        private static async Task DeniedAsync(Func<Task> action, int expected)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == expected) { return; }
            throw new InvalidDataException("Scoped assignment control did not enforce status " + expected + ".");
        }
    }
}
