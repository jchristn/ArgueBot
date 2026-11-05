namespace ArgueBot.Server.Services
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;

    /// <summary>Authenticates every non-public request and enforces its access class before any handler runs.</summary>
    internal static class RequestGate
    {
        internal static async Task AuthenticateAsync(HttpContextBase http, RequestAuthenticator authenticator)
        {
            RequestContext received = Api.Context(http);
            string method = http.Request.Method.ToString().ToUpperInvariant(); string path = http.Request.Url.RawWithoutQuery;
            string requestType = method + " " + path;
            string authorization = http.Request.Headers["Authorization"] ?? "";
            string bearer = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization.Substring(7).Trim() : "";
            string header = (http.Request.Headers["x-token"] ?? "").Trim();
            try
            {
                if (authorization.Length > 0 && bearer.Length == 0) throw new DomainException(401, "unsupported_scheme", "Use Authorization: Bearer with a session token or credential token.");
                if (bearer.Length > 0 && header.Length > 0 && !string.Equals(bearer, header, StringComparison.Ordinal)) throw new DomainException(401, "conflicting_credentials", "Authorization and x-token carry different tokens. Send one token.");
                if ((http.Request.Headers["x-password"] ?? "").Length > 0) throw new DomainException(401, "password_not_accepted", "Passwords are accepted only by POST /v2.0/api/token.");
                string presented = bearer.Length > 0 ? bearer : header;
                if (presented.Length == 0) throw new DomainException(401, "authentication_required", "Sign in or send a bearer token.");
                RequestContext context = await authenticator.AuthenticateAsync(presented, bearer.Length > 0 ? "Bearer" : "XToken", received, http.Token).ConfigureAwait(false);
                http.Metadata = context;
            }
            catch (DomainException ex) when (ex.StatusCode == 401)
            {
                await authenticator.RecordAsync(new AuditEvent { Operation = "AuthFailure", Outcome = "failure", StatusCode = 401, ErrorCode = ex.Code, AuthenticationResult = ex.Code == "authentication_required" ? "NotFound" : ex.Code.EndsWith("inactive", StringComparison.Ordinal) ? "Inactive" : "Invalid", Reason = ex.Message, RequestType = requestType, Method = method, Path = path, SourceIp = received.SourceIp, CorrelationId = received.CorrelationId }, http.Token).ConfigureAwait(false);
                http.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"ArgueBot\"";
                throw;
            }
            RequestContext signedIn = Api.Context(http);
            string tenantHeader = (http.Request.Headers["x-tenant-id"] ?? "").Trim();
            string access = RouteAccess.Classify(method, path);
            if (tenantHeader.Length > 0 && tenantHeader != signedIn.TenantId && signedIn.IsAdmin && access == RouteAccess.TenantAdmin)
            {
                // System administrators may manage another tenant's users, sessions and audit log. The bypass is always audited.
                await authenticator.RequireTenantAsync(tenantHeader, http.Token).ConfigureAwait(false);
                await authenticator.RecordAsync(new AuditEvent { TenantId = tenantHeader, UserId = signedIn.UserId, Operation = "AuthorizationBypass", Outcome = "success", StatusCode = 0, AuthenticationResult = "Success", AuthorizationResult = "Permitted", Reason = "System administrator acting in tenant " + tenantHeader + " from " + signedIn.TenantId, RequestType = requestType, Method = method, Path = path, SourceIp = signedIn.SourceIp, CorrelationId = signedIn.CorrelationId, SessionId = signedIn.SessionId, CredentialId = signedIn.CredentialId, PrincipalType = signedIn.PrincipalType, AuthScheme = signedIn.AuthScheme }, http.Token).ConfigureAwait(false);
                signedIn.TenantId = tenantHeader;
                return;
            }
            string reason = tenantHeader.Length > 0 && tenantHeader != signedIn.TenantId ? "The x-tenant-id header does not match the token's tenant." : !RouteAccess.Permits(access, signedIn.IsAdmin, signedIn.IsTenantAdmin) ? (access == RouteAccess.SystemAdmin ? "This action needs a system administrator." : "This action needs a tenant administrator.") : "";
            if (reason.Length == 0) return;
            string code = tenantHeader.Length > 0 && tenantHeader != signedIn.TenantId ? "tenant_mismatch" : "forbidden";
            await authenticator.RecordAsync(new AuditEvent { TenantId = signedIn.TenantId, UserId = signedIn.UserId, Operation = "AuthorizationDenied", Outcome = "denied", StatusCode = 403, ErrorCode = code, AuthenticationResult = "Success", AuthorizationResult = "DeniedImplicit", Reason = reason, RequestType = requestType, Method = method, Path = path, SourceIp = signedIn.SourceIp, CorrelationId = signedIn.CorrelationId, SessionId = signedIn.SessionId, CredentialId = signedIn.CredentialId, PrincipalType = signedIn.PrincipalType, AuthScheme = signedIn.AuthScheme }, http.Token).ConfigureAwait(false);
            await Api.SendAsync(http, new AuthorizationError { Code = code, Message = reason, Reason = reason, RequiredPermission = access, RequestType = requestType, TenantId = signedIn.TenantId, RequestId = signedIn.CorrelationId }, 403).ConfigureAwait(false);
        }
    }
}
