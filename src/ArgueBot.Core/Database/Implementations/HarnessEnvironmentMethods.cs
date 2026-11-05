namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Helpers;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal static class HarnessEnvironmentMethods
    {
        internal static async Task SaveAsync(DbConnection connection, DbTransaction transaction, string tenant, string owner, bool restricted, List<string> names, CancellationToken token)
        {
            await ExecuteAsync(connection, transaction, "DELETE FROM connection_environment_owners WHERE tenant_id=@tenant AND id=@id", token, P("tenant", tenant, "id", owner)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO connection_environment_owners(id,tenant_id,restricted,created_utc,updated_utc) VALUES(@id,@tenant,@restricted,@now,@now)", token, P("tenant", tenant, "id", owner, "restricted", restricted ? 1 : 0)).ConfigureAwait(false);
            for (int index = 0; index < names.Count; index++) await ExecuteAsync(connection, transaction, "INSERT INTO connection_environment_variables(id,tenant_id,owner_id,position,name,created_utc,updated_utc) VALUES(@id,@tenant,@owner,@position,@name,@now,@now)", token, P("id", Ids.New("env_"), "tenant", tenant, "owner", owner, "position", index, "name", names[index])).ConfigureAwait(false);
        }
        internal static async Task<bool> LoadAsync(DbConnection connection, DbTransaction? transaction, string tenant, string owner, List<string> names, CancellationToken token)
        {
            bool restricted = false;
            using (DbCommand command = Command(connection, transaction, "SELECT restricted FROM connection_environment_owners WHERE tenant_id=@tenant AND id=@id", P("tenant", tenant, "id", owner)))
            {
                object? value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                restricted = value != null && value != DBNull.Value && Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
            }
            using (DbCommand command = Command(connection, transaction, "SELECT name FROM connection_environment_variables WHERE tenant_id=@tenant AND owner_id=@id ORDER BY position", P("tenant", tenant, "id", owner)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) names.Add(reader.GetString(0)); }
            return restricted;
        }
    }
}
