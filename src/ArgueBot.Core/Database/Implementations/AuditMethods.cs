namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Scoped immutable security accounting. Privileged database operators remain outside application-level immutability.</summary>
    public sealed class AuditMethods : IAuditMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal AuditMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Read a stable newest-first page, scoped to the validated tenant unless the caller is a system administrator.</summary>
        public async Task<AuditPage> EnumerateAsync(RequestContext context, string operation = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(operation);
            if (!context.IsAuthenticated || (!context.IsAdmin && !context.IsTenantAdmin)) throw new DomainException(403, "forbidden", "Audit access requires a tenant or system administrator.");
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || operation.Length > 100) throw new DomainException(400, "invalid_filter", "Use a positive page, size one through one hundred and a bounded operation code.");
            string scope = "(@admin=1 OR tenant_id=@tenant) AND (@operation='' OR operation=@operation)";
            AuditPage page = new AuditPage { PageNumber = pageNumber, PageSize = pageSize };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM audit_events WHERE " + scope, token, P("admin", context.IsAdmin ? 1 : 0, "tenant", context.TenantId, "operation", operation)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM audit_events WHERE " + scope + " ORDER BY created_utc DESC,id DESC LIMIT @limit OFFSET @offset", P("admin", context.IsAdmin ? 1 : 0, "tenant", context.TenantId, "operation", operation, "limit", pageSize, "offset", checked((long)(pageNumber - 1) * pageSize))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) page.Items.Add(new AuditEntry { Id = S(reader, "id"), TenantId = reader.IsDBNull(reader.GetOrdinal("tenant_id")) ? null : S(reader, "tenant_id"), UserId = reader.IsDBNull(reader.GetOrdinal("user_id")) ? null : S(reader, "user_id"), Operation = S(reader, "operation"), Outcome = S(reader, "outcome"), SessionId = S(reader, "session_id"), AuthScheme = S(reader, "auth_scheme"), CorrelationId = S(reader, "correlation_id"), ResourceType = S(reader, "resource_type"), ResourceId = S(reader, "resource_id"), ErrorCode = S(reader, "error_code"), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) });
                }
            }
            return page;
        }
        /// <summary>Record a denial using validated context only. Anonymous attempts remain global and cannot inject tenant/user claims into audit records.</summary>
        public Task RecordDeniedRequestAsync(RequestContext context, int status, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (status != 401 && status != 403 && status != 429) throw new ArgumentOutOfRangeException(nameof(status));
            return _Database.WriteAsync(async (connection, transaction) =>
            {
                await AppendAsync(connection, transaction, context.IsAuthenticated ? context : new RequestContext { CorrelationId = context.CorrelationId }, "http.authorization", "denied", "Request", "", status == 401 ? "unauthenticated" : status == 429 ? "rate_limited" : "forbidden", token).ConfigureAwait(false);
            }, token);
        }
        internal static Task<int> AppendAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string operation, string outcome, string resourceType, string resourceId, string errorCode, CancellationToken token)
        {
            return ExecuteAsync(connection, transaction, "INSERT INTO audit_events(id,tenant_id,user_id,operation,outcome,session_id,auth_scheme,correlation_id,resource_type,resource_id,error_code,created_utc,updated_utc) VALUES(@id,@tenant,@user,@operation,@outcome,@session,@scheme,@correlation,@type,@resource,@error,@now,@now)", token, P("id", Ids.New("aud_"), "tenant", context.IsAuthenticated ? context.TenantId : null, "user", context.IsAuthenticated ? context.UserId : null, "operation", operation, "outcome", outcome, "session", context.SessionId, "scheme", context.AuthScheme, "correlation", context.CorrelationId, "type", resourceType, "resource", resourceId, "error", errorCode));
        }
    }
}
