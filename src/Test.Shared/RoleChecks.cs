namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
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
    using ArgueBot.Core.Services;

    internal static class RoleChecks
    {
        internal static async Task SqliteAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-role-admin-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, ForeignKeys = true, Pooling = false }.ToString()))
                { await database.InitializeAsync(token).ConfigureAwait(false); await connection.OpenAsync(token).ConfigureAwait(false); await RunAsync(database, connection, token).ConfigureAwait(false); }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        internal static async Task RunAsync(DatabaseDriverBase database, DbConnection connection, CancellationToken token)
        {
            RequestContext owner = new RequestContext { IsAuthenticated = true, TenantId = "ten_local", UserId = "usr_local" };
            string suffix = Guid.NewGuid().ToString("N"); List<string> created = new List<string>();
            UserRecord viewer = await database.Users.CreateAsync(new RequestContext { IsAuthenticated = true, TenantId = owner.TenantId, UserId = owner.UserId, IsAdmin = true }, new UserRequest { Name = "Role viewer", Email = suffix + "@example.invalid" }, token).ConfigureAwait(false);
            RequestContext spoofed = new RequestContext { IsAuthenticated = true, TenantId = owner.TenantId, UserId = viewer.Id, IsAdmin = true, IsTenantAdmin = true };
            RequestContext foreign = new RequestContext { IsAuthenticated = true, TenantId = "ten_" + suffix, UserId = "usr_" + suffix };
            string? foreignRole = null;
            try
            {
                RolePage builtins = await database.Roles.EnumerateAsync(owner, "Viewer", token: token).ConfigureAwait(false);
                if (builtins.Items.Count != 1 || !builtins.Items[0].Protected || builtins.Items[0].TenantId != null) throw new InvalidDataException("Built-in role metadata is not globally visible and protected.");
                await DeniedAsync(async () => { await database.Roles.EnumerateAsync(spoofed, token: token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Roles.CreateAsync(owner, new RoleRequest { Name = " viewer " }, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Roles.UpdateAsync(owner, builtins.Items[0].Id, new RoleRequest { Name = "Viewer", ExpectedRevision = 1 }, token).ConfigureAwait(false); }, 403).ConfigureAwait(false);
                RoleRecord role = await database.Roles.CreateAsync(owner, new RoleRequest { Name = "Role %+_議論-" + suffix }, token).ConfigureAwait(false); created.Add(role.Id);
                RoleRecord clone = await database.Roles.CreateAsync(owner, new RoleRequest { Name = "Clone " + suffix, CloneRoleId = builtins.Items[0].Id }, token).ConfigureAwait(false); created.Add(clone.Id);
                using (DbCommand command = Command(connection, "SELECT COUNT(*) FROM authorization_role_permissions WHERE tenant_id=@tenant AND role_id=@role AND active=1", "tenant", owner.TenantId, "role", clone.Id))
                { if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1) throw new InvalidDataException("Cloning lost the built-in permission mapping."); }
                RolePage search = await database.Roles.EnumerateAsync(owner, "%+_議論-" + suffix, pageSize: 1, token: token).ConfigureAwait(false);
                if (search.TotalCount != 1 || search.Items.Count != 1 || search.Items[0].Id != role.Id) throw new InvalidDataException("Literal Unicode role search or pagination returned unintended definitions.");
                await DeniedAsync(async () => { await database.Roles.CreateAsync(owner, new RoleRequest { Name = role.Name.ToUpperInvariant() }, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                role = await database.Roles.UpdateAsync(owner, role.Id, new RoleRequest { Name = role.Name, Active = false, ExpectedRevision = role.Revision }, token).ConfigureAwait(false);
                if (role.Active || role.Revision != 2) throw new InvalidDataException("Role deactivation lost optimistic concurrency metadata.");
                await DeniedAsync(async () => { await database.Roles.UpdateAsync(owner, role.Id, new RoleRequest { Name = role.Name, ExpectedRevision = 1 }, token).ConfigureAwait(false); }, 409).ConfigureAwait(false);
                using (DbCommand command = Command(connection, "SELECT COUNT(*) FROM audit_events WHERE tenant_id=@tenant AND resource_type='Role' AND resource_id=@id AND operation='role.update'", "tenant", owner.TenantId, "id", role.Id))
                { if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1) throw new InvalidDataException("Role updates lost atomic audit accounting or a stale write produced an extra event."); }
                using (DbCommand command = Command(connection, "INSERT INTO tenants(id,name,created_utc,updated_utc) VALUES(@tenant,'Foreign role fixture',@now,@now); INSERT INTO users(id,tenant_id,name,role,is_tenant_admin,created_utc,updated_utc) VALUES(@user,@tenant,'Foreign administrator','viewer',1,@now,@now)", "tenant", foreign.TenantId, "user", foreign.UserId))
                { await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); }
                foreignRole = (await database.Roles.CreateAsync(foreign, new RoleRequest { Name = "Foreign " + suffix }, token).ConfigureAwait(false)).Id;
                await DeniedAsync(async () => { await database.Roles.ReadAsync(owner, foreignRole, token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Roles.CreateAsync(owner, new RoleRequest { Name = "Forbidden clone " + suffix, CloneRoleId = foreignRole }, token).ConfigureAwait(false); }, 404).ConfigureAwait(false);
                if ((await database.Roles.EnumerateAsync(owner, "Foreign " + suffix, token: token).ConfigureAwait(false)).TotalCount != 0) throw new InvalidDataException("Role list leaked foreign tenant metadata or counts.");
                if ((await database.Roles.ReadAsync(foreign, foreignRole, token).ConfigureAwait(false)).TenantId != foreign.TenantId) throw new InvalidDataException("Tenant administrator role creation lost its tenant binding.");
            }
            finally
            {
                foreach (string id in created)
                {
                    RoleRecord current = await database.Roles.ReadAsync(owner, id, CancellationToken.None).ConfigureAwait(false);
                    if (current.Active) await database.Roles.UpdateAsync(owner, id, new RoleRequest { Name = current.Name, Active = false, ExpectedRevision = current.Revision }, CancellationToken.None).ConfigureAwait(false);
                }
                if (foreignRole != null)
                {
                    RoleRecord current = await database.Roles.ReadAsync(foreign, foreignRole, CancellationToken.None).ConfigureAwait(false);
                    await database.Roles.UpdateAsync(foreign, foreignRole, new RoleRequest { Name = current.Name, Active = false, ExpectedRevision = current.Revision }, CancellationToken.None).ConfigureAwait(false);
                    using (DbCommand command = Command(connection, "UPDATE users SET active=0 WHERE tenant_id=@tenant AND id=@user", "tenant", foreign.TenantId, "user", foreign.UserId)) { await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false); }
                }
                using (DbCommand command = Command(connection, "UPDATE users SET active=0 WHERE tenant_id=@tenant AND id=@user", "tenant", owner.TenantId, "user", viewer.Id)) { await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false); }
            }
        }
        private static async Task DeniedAsync(Func<Task> action, int expected)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == expected) { return; }
            throw new InvalidDataException("Role control did not enforce status " + expected + ".");
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
