namespace ArgueBot.Core.Database
{
    using System;
    using System.Data.Common;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Security;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal static class AuthorizationSeeder
    {
        internal static async Task ApplyAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
        {
            foreach (BuiltInRoleDefinition definition in BuiltInRoles.All)
            {
                object? existing = await ScalarAsync(connection, transaction, "SELECT id FROM authorization_roles WHERE tenant_id IS NULL AND lower(name)=lower(@name)", token, P("name", definition.Name)).ConfigureAwait(false);
                string id = existing is string found ? found : Ids.New("rol_");
                if (existing == null)
                    await ExecuteAsync(connection, transaction, "INSERT INTO authorization_roles(id,tenant_id,name,builtin,protected,active,created_utc,updated_utc) VALUES(@id,NULL,@name,1,1,1,@now,@now)", token, P("id", id, "name", definition.Name)).ConfigureAwait(false);
                else
                    await ExecuteAsync(connection, transaction, "UPDATE authorization_roles SET name=@name,builtin=1,protected=1,active=1,updated_utc=@now WHERE id=@id AND tenant_id IS NULL", token, P("id", id, "name", definition.Name)).ConfigureAwait(false);
                // Built-in definitions are migration-owned. Refresh mappings without changing the role's historical ID.
                await ExecuteAsync(connection, transaction, "DELETE FROM authorization_role_permissions WHERE role_id=@id AND tenant_id IS NULL AND id<>@mapping", token, P("id", id, "mapping", id + "_map")).ConfigureAwait(false);
                if (definition.ResourceTypes.Length == 0) continue;
                string permission = id + "_default";
                await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO authorization_permissions(id,tenant_id,name,resource_types,operations,effect,requires_ownership,protected,active,created_utc,updated_utc) VALUES(@id,NULL,@name,@resources,@operations,'Permit',@ownership,1,1,@now,@now)", token, P("id", permission, "name", definition.Name, "resources", JsonSerializer.Serialize(definition.ResourceTypes), "operations", JsonSerializer.Serialize(definition.Operations), "ownership", definition.RequiresOwnership ? 1 : 0)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE authorization_permissions SET name=@name,resource_types=@resources,operations=@operations,effect='Permit',requires_ownership=@ownership,protected=1,active=1,updated_utc=@now WHERE id=@id AND tenant_id IS NULL", token, P("id", permission, "name", definition.Name, "resources", JsonSerializer.Serialize(definition.ResourceTypes), "operations", JsonSerializer.Serialize(definition.Operations), "ownership", definition.RequiresOwnership ? 1 : 0)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO authorization_role_permissions(id,tenant_id,role_id,permission_id,protected,active,created_utc,updated_utc) VALUES(@id,NULL,@role,@permission,1,1,@now,@now)", token, P("id", id + "_map", "role", id, "permission", permission)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE authorization_role_permissions SET permission_id=@permission,protected=1,active=1,updated_utc=@now WHERE id=@id AND role_id=@role AND tenant_id IS NULL", token, P("id", id + "_map", "role", id, "permission", permission)).ConfigureAwait(false);
            }
        }
    }
}
