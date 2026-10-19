namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class AuditChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, CancellationToken token)
        {
            string tenant = "ten_audit_" + Guid.NewGuid().ToString("N");
            RequestContext own = new RequestContext { IsAuthenticated = true, TenantId = tenant, UserId = "usr_audit", IsTenantAdmin = true, SessionId = "ses_fixture", AuthScheme = "XToken", CorrelationId = "audit-fixture" };
            await database.Audit.RecordDeniedRequestAsync(own, 403, token).ConfigureAwait(false);
            await database.Audit.RecordDeniedRequestAsync(new RequestContext { TenantId = tenant, UserId = "usr_spoofed", SessionId = "ses_spoofed", AuthScheme = "untrusted", CorrelationId = "anonymous-fixture" }, 401, token).ConfigureAwait(false);
            AuditPage page = await database.Audit.EnumerateAsync(own, "http.authorization", 1, 1, token).ConfigureAwait(false);
            AuditEntry entry = page.Items.Single();
            if (page.TotalCount != 1 || entry.TenantId != tenant || entry.UserId != own.UserId || entry.SessionId != own.SessionId || entry.Outcome != "denied" || entry.ErrorCode != "forbidden" || entry.CorrelationId != "audit-fixture") throw new InvalidDataException("Scoped audit accounting lost validated identity or accepted anonymous tenant spoofing.");
            AuditPage other = await database.Audit.EnumerateAsync(new RequestContext { IsAuthenticated = true, TenantId = tenant + "_other", IsTenantAdmin = true }, token: token).ConfigureAwait(false);
            if (other.TotalCount != 0) throw new InvalidDataException("Another tenant could enumerate security events.");
            AuditPage global = await database.Audit.EnumerateAsync(new RequestContext { IsAuthenticated = true, IsAdmin = true }, "http.authorization", 1, 100, token).ConfigureAwait(false);
            if (!global.Items.Any(item => item.TenantId == null && item.UserId == null && item.SessionId.Length == 0 && item.AuthScheme.Length == 0 && item.CorrelationId == "anonymous-fixture")) throw new InvalidDataException("Anonymous accounting trusted unvalidated principal fields.");
            bool denied = false;
            try { await database.Audit.EnumerateAsync(new RequestContext { IsAuthenticated = true, TenantId = tenant, Role = "admin" }, token: token).ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == 403) { denied = true; }
            if (!denied) throw new InvalidDataException("An admin role name bypassed audit permissions.");
            AuditPage empty = await database.Audit.EnumerateAsync(own, "no-such-operation", token: token).ConfigureAwait(false);
            if (empty.TotalCount != 0 || empty.Items.Count != 0) throw new InvalidDataException("Audit operation filtering returned unrelated events.");
        }
    }
}
