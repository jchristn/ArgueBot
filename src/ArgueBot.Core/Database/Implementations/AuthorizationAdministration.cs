namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal static class AuthorizationAdministration
    {
        internal static async Task RequireAsync(DatabaseDriverBase database, RequestContext context, CancellationToken token)
        {
            ResolvedAuthorization resolved = await database.Authorization.ResolveUserAsync(context, token).ConfigureAwait(false);
            if (!resolved.Principal.IsActive || (!resolved.Principal.IsAdmin && !resolved.Principal.IsTenantAdmin)) throw new DomainException(403, "forbidden", "Authorization administration requires current tenant or system administrator authority.");
        }
        internal static async Task RequireAsync(DbConnection connection, DbTransaction transaction, RequestContext context, CancellationToken token)
        {
            long active = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE u.tenant_id=@tenant AND u.id=@user AND u.active=1 AND t.active=1 AND (u.is_admin=1 OR u.is_tenant_admin=1)", token, P("tenant", context.TenantId, "user", context.UserId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (active != 1) throw new DomainException(403, "forbidden", "Current tenant or system administrator authority is required.");
        }
    }
}
