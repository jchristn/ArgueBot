namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using System;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Writes security audit records. Values are truncated so a hostile path or user agent cannot bloat the store.</summary>
    internal static class AuditLog
    {
        internal static Task WriteAsync(DatabaseDriverBase database, AuditEvent entry, CancellationToken token)
        {
            return database.WriteAsync((connection, transaction) => WriteAsync(connection, transaction, entry, token), token);
        }

        internal static Task<int> WriteAsync(DbConnection connection, DbTransaction? transaction, AuditEvent entry, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return ExecuteAsync(connection, transaction, "INSERT INTO audit_events(id,tenant_id,user_id,operation,outcome,session_id,auth_scheme,correlation_id,resource_type,resource_id,error_code,principal_type,credential_id,request_type,method,path,source_ip,authentication_result,authorization_result,reason,status_code,active,created_utc,updated_utc) VALUES(@id,@tenant,@user,@operation,@outcome,@session,@scheme,@correlation,@resourceType,@resourceId,@error,@principal,@credential,@requestType,@method,@path,@ip,@authn,@authz,@reason,@status,1,@now,@now)", token,
                P("id", Ids.New("aud_"), "tenant", entry.TenantId.Length == 0 ? null : Cut(entry.TenantId, 64), "user", entry.UserId.Length == 0 ? null : Cut(entry.UserId, 64), "operation", Cut(entry.Operation, 64), "outcome", Cut(entry.Outcome, 32),
                  "session", Cut(entry.SessionId, 64), "scheme", Cut(entry.AuthScheme, 32), "correlation", Cut(entry.CorrelationId, 64), "resourceType", Cut(entry.ResourceType, 64), "resourceId", Cut(entry.ResourceId, 128), "error", Cut(entry.ErrorCode, 64),
                  "principal", Cut(entry.PrincipalType, 32), "credential", Cut(entry.CredentialId, 64), "requestType", Cut(entry.RequestType, 300), "method", Cut(entry.Method, 16), "path", Cut(entry.Path, 256), "ip", Cut(entry.SourceIp, 64),
                  "authn", Cut(entry.AuthenticationResult, 32), "authz", Cut(entry.AuthorizationResult, 32), "reason", Cut(entry.Reason, 300), "status", entry.StatusCode));
        }

        private static string Cut(string value, int length) { return value.Length <= length ? value : value.Substring(0, length); }
    }
}
