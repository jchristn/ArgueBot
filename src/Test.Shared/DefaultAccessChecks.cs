namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;

    internal static class DefaultAccessChecks
    {
        internal static async Task SqliteAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-default-access-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); string secret; string userId; TokenProtector protector;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false); await RunAsync(database, token).ConfigureAwait(false);
                    userId = (await database.Users.CreateAsync(Owner(), new UserRequest { Name = "Schema twelve operator", Email = "migration@example.invalid", Role = "operator" }, token).ConfigureAwait(false)).Id;
                    protector = new TokenProtector(paths); secret = await new SessionMethods(database, protector).IssueLocalAsync(token).ConfigureAwait(false);
                }
                using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString()))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "DELETE FROM authorization_user_assignments WHERE system_kind<>''; DROP INDEX ix_assignments_system_kind; ALTER TABLE authorization_user_assignments DROP COLUMN system_kind; DELETE FROM schema_migrations WHERE version=13;";
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                using (SqliteDatabaseDriver upgraded = new SqliteDatabaseDriver(paths))
                {
                    await upgraded.InitializeAsync(token).ConfigureAwait(false);
                    if ((await new SessionMethods(upgraded, protector).AuthenticateAsync(secret, token).ConfigureAwait(false))?.UserId != "usr_local") throw new InvalidDataException("Default-access migration changed existing sessions.");
                    UserRecord user = await upgraded.Users.ReadAsync(Owner(), userId, token).ConfigureAwait(false);
                    UserRoleAssignmentPage assignments = await upgraded.UserRoleAssignments.EnumerateAsync(Owner(), user.Id, token: token).ConfigureAwait(false);
                    if (!user.Active || user.Role != "operator" || assignments.Items.Count != 2 || assignments.Items.Any(item => !item.Protected || !item.Active || item.SystemKind.Length == 0)) throw new InvalidDataException("Schema-12 identity migration lost preserved default access or protected history.");
                    await CheckAsync(upgraded, Context(user.Id), "Run", "Execute", "scope", user.Id, true, token).ConfigureAwait(false);
                }
                if (Directory.GetFiles(Path.Combine(home, "backups"), "before-schema-12-*.db").Length != 1) throw new InvalidDataException("Default-access upgrade skipped its protected pre-upgrade backup.");
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        internal static async Task RunAsync(DatabaseDriverBase database, CancellationToken token)
        {
            string suffix = Guid.NewGuid().ToString("N"); RequestContext owner = Owner();
            UserRecord user = await database.Users.CreateAsync(owner, new UserRequest { Name = "Owned default fixture", Email = suffix + "@example.invalid" }, token).ConfigureAwait(false);
            RequestContext subject = Context(user.Id); RoleRecord? denyRole = null; PermissionRecord? deny = null; UserRoleAssignmentRecord? extra = null;
            try
            {
                UserRoleAssignmentPage baseline = await database.UserRoleAssignments.EnumerateAsync(owner, user.Id, token: token).ConfigureAwait(false);
                if (baseline.TotalCount != 2 || baseline.Items.Any(item => !item.Protected || !item.InheritsToChildren || item.ResourceScope != "Tenant")) throw new InvalidDataException("New identities did not receive protected owned access and tenant presence.");
                UserRoleAssignmentRecord defaultAccess = baseline.Items.Single(item => item.SystemKind == "DefaultAccess");
                if (defaultAccess.RoleName != "OwnedReader") throw new InvalidDataException("Viewer default was not translated to its owned-reader role.");
                await CheckAsync(database, subject, "Conversation", "Read", "scope", user.Id, true, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "Conversation", "Read", "scope", owner.UserId, false, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "Conversation", "Create", "", user.Id, false, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "Run", "Execute", "scope", user.Id, false, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "RunnerProfile", "Read", "profile", user.Id, true, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "RunnerProfile", "Admin", "profile", user.Id, false, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "User", "Read", user.Id, user.Id, true, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "User", "Admin", user.Id, user.Id, false, token).ConfigureAwait(false);
                user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = user.Email, Role = "operator", ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                UserRoleAssignmentRecord changed = (await database.UserRoleAssignments.EnumerateAsync(owner, user.Id, token: token).ConfigureAwait(false)).Items.Single(item => item.SystemKind == "DefaultAccess");
                if (changed.Id != defaultAccess.Id || changed.RoleName != "OwnedOperator" || changed.Revision != defaultAccess.Revision + 1) throw new InvalidDataException("Default access synchronization replaced history or lost its revision.");
                await CheckAsync(database, subject, "Run", "Execute", "scope", user.Id, true, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "RunnerProfile", "Admin", "profile", user.Id, true, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "CliHarness", "Execute", "profile", user.Id, true, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "RunnerProfile", "Admin", "profile", owner.UserId, false, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "User", "Admin", user.Id, user.Id, false, token).ConfigureAwait(false);
                deny = await database.Permissions.CreateAsync(owner, new PermissionRequest { Name = "Deny own generation " + suffix, ResourceTypes = new List<string> { "Run" }, Operations = new List<string> { "Execute" }, Effect = "Deny" }, token).ConfigureAwait(false);
                denyRole = await database.Roles.CreateAsync(owner, new RoleRequest { Name = "Deny own generation " + suffix }, token).ConfigureAwait(false);
                denyRole = await database.RolePermissions.ReplaceAsync(owner, denyRole.Id, new RolePermissionsRequest { PermissionIds = new List<string> { deny.Id }, ExpectedRevision = denyRole.Revision }, token).ConfigureAwait(false);
                extra = await database.UserRoleAssignments.CreateAsync(owner, user.Id, new UserRoleAssignmentRequest { RoleId = denyRole.Id, ResourceScope = "Resource", ResourceId = "scope" }, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "Run", "Execute", "scope", user.Id, false, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "Run", "Execute", "other", user.Id, true, token).ConfigureAwait(false);
                await RoundTripAsync(database, owner, user.Id, changed.Id, token).ConfigureAwait(false);
                user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = user.Email, Role = "viewer", ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                await CheckAsync(database, subject, "Run", "Execute", "other", user.Id, false, token).ConfigureAwait(false);
            }
            finally
            {
                if (extra != null) await database.UserRoleAssignments.UpdateAsync(owner, user.Id, extra.Id, new UserRoleAssignmentRequest { RoleId = extra.RoleId, ResourceScope = extra.ResourceScope, ResourceId = extra.ResourceId, Active = false, ExpectedRevision = extra.Revision }, CancellationToken.None).ConfigureAwait(false);
                if (denyRole != null) await database.Roles.UpdateAsync(owner, denyRole.Id, new RoleRequest { Name = denyRole.Name, Active = false, ExpectedRevision = denyRole.Revision }, CancellationToken.None).ConfigureAwait(false);
                if (deny != null) await database.Permissions.UpdateAsync(owner, deny.Id, new PermissionRequest { Name = deny.Name, ResourceTypes = deny.ResourceTypes, Operations = deny.Operations, Effect = deny.Effect, Active = false, ExpectedRevision = deny.Revision }, CancellationToken.None).ConfigureAwait(false);
                await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = user.Email, Role = user.Role, Active = false, ExpectedRevision = user.Revision }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static async Task RoundTripAsync(DatabaseDriverBase source, RequestContext owner, string user, string assignment, CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-default-restore-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home); paths.EnsureDirectories(); string snapshot = Path.Combine(home, "snapshot.jsonl");
                await source.ExportSnapshotAsync(snapshot, token).ConfigureAwait(false);
                using (SqliteDatabaseDriver target = new SqliteDatabaseDriver(paths))
                {
                    await target.InitializeAsync(token).ConfigureAwait(false); await target.RestoreSnapshotAsync(snapshot, token).ConfigureAwait(false); await target.InitializeAsync(token).ConfigureAwait(false);
                    UserRoleAssignmentRecord restored = (await target.UserRoleAssignments.EnumerateAsync(owner, user, token: token).ConfigureAwait(false)).Items.Single(item => item.SystemKind == "DefaultAccess");
                    if (restored.Id != assignment || restored.RoleName != "OwnedOperator") throw new InvalidDataException("Portable recovery replaced default membership identity or policy.");
                    await CheckAsync(target, Context(user), "Run", "Execute", "scope", user, false, token).ConfigureAwait(false);
                    await CheckAsync(target, Context(user), "Run", "Execute", "other", user, true, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task CheckAsync(DatabaseDriverBase database, RequestContext subject, string type, string operation, string id, string owner, bool expected, CancellationToken token)
        {
            AuthorizationDecision decision = await database.Authorization.EvaluateUserAsync(subject, new AuthorizationTarget { TenantId = subject.TenantId, ResourceType = type, Operation = operation, ResourceId = id, OwnerUserId = owner }, token).ConfigureAwait(false);
            if (decision.Allowed != expected) throw new InvalidDataException("Protected default policy changed expected authority for " + type + "/" + operation + ": " + decision.Reason);
        }
        private static RequestContext Owner() { return new RequestContext { IsAuthenticated = true, TenantId = "ten_local", UserId = "usr_local", IsAdmin = true, Role = "admin" }; }
        private static RequestContext Context(string user) { return new RequestContext { IsAuthenticated = true, TenantId = "ten_local", UserId = user, Role = "viewer" }; }
    }
}
