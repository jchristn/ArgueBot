namespace ArgueBot.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Security;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal static class RoleReferenceResolver
    {
        internal static async Task<ResolvedRoleReference?> ResolveAsync(DbConnection connection, DbTransaction transaction, string tenant, string id, string name, CancellationToken token)
        {
            if (id.Length > 0)
            {
                using (DbCommand command = Command(connection, transaction, "SELECT id,name,active FROM authorization_roles WHERE id=@id AND (tenant_id=@tenant OR tenant_id IS NULL)", P("id", id, "tenant", tenant)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { if (await reader.ReadAsync(token).ConfigureAwait(false)) return Read(reader); }
            }
            BuiltInRoleDefinition? baseline = BuiltInRoles.Find(name);
            if (name.Length > 0)
            {
                string scope = baseline == null ? "(tenant_id=@tenant OR tenant_id IS NULL)" : "tenant_id IS NULL";
                using (DbCommand command = Command(connection, transaction, "SELECT id,name,active FROM authorization_roles WHERE lower(name)=lower(@name) AND " + scope + " ORDER BY CASE WHEN tenant_id IS NULL THEN 0 ELSE 1 END LIMIT 1", P("name", name, "tenant", tenant)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { if (await reader.ReadAsync(token).ConfigureAwait(false)) return Read(reader); }
            }
            return baseline == null ? null : new ResolvedRoleReference { Name = baseline.Name, Active = true, BaselineOnly = true };
        }
        private static ResolvedRoleReference Read(DbDataReader reader)
        { return new ResolvedRoleReference { Id = S(reader, "id"), Name = S(reader, "name"), Active = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0 }; }
    }
}
