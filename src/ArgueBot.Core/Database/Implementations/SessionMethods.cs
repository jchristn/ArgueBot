namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Revocable opaque local sessions. Only token digests are persisted.</summary>
    public sealed class SessionMethods : ISessionMethods
    {
        private readonly DatabaseDriverBase _Database;
        private readonly TokenProtector _Protector;
        /// <summary>Create a session store using the shared database.</summary>
        public SessionMethods(DatabaseDriverBase database, TokenProtector protector) { _Database = database; _Protector = protector; }
        /// <summary>Issue a one-hour local session after successful protected enrollment.</summary>
        public async Task<string> IssueLocalAsync(CancellationToken token = default)
        {
            string sessionId = Ids.New("ses_");
            DateTime expiry = DateTime.UtcNow.AddHours(1);
            string secret = _Protector.Protect(new SessionClaims { SessionId = sessionId, TenantId = "ten_local", UserId = "usr_local", IssuedUtc = DateTime.UtcNow, ExpiresUtc = expiry, TokenId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) });
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO auth_sessions(id,tenant_id,user_id,token_hash,expires_utc,created_utc,updated_utc) VALUES(@id,'ten_local','usr_local',@hash,@expires,@now,@now)", token, P("id", sessionId, "hash", Hash(secret), "expires", expiry.ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, new RequestContext { IsAuthenticated = true, TenantId = "ten_local", UserId = "usr_local", SessionId = sessionId, AuthScheme = "LocalEnrollment" }, "session.issue", "issued", "Session", sessionId, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return secret;
        }
        /// <summary>Exchange tenant-local password-equivalent material for a one-hour revocable session. The transport must enforce TLS and rate-limit this operation.</summary>
        public async Task<string> LoginAsync(LoginRequest request, string correlationId = "", CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            string tenant = request.TenantId ?? ""; string email = request.Email ?? ""; string password = request.PasswordSha256 ?? "";
            bool shape = tenant.Length > 0 && tenant.Length <= 64 && email.Length <= 254 && Regex.IsMatch(password, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant);
            string issued = "";
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                string user = ""; string stored = ""; string salt = ""; int iterations = 0;
                if (shape)
                {
                    using (DbCommand command = Command(connection, transaction, "SELECT u.id,u.password_sha256,u.password_salt,u.password_iterations FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE u.tenant_id=@tenant AND u.email=@email AND u.active=1 AND t.active=1", P("tenant", tenant, "email", email.ToLowerInvariant())))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { if (await reader.ReadAsync(token).ConfigureAwait(false)) { user = S(reader, "id"); stored = S(reader, "password_sha256"); salt = S(reader, "password_salt"); iterations = Convert.ToInt32(reader["password_iterations"], CultureInfo.InvariantCulture); } }
                }
                bool matches = PasswordVerifier.Matches(password, stored, salt, iterations);
                RequestContext context = new RequestContext { CorrelationId = correlationId };
                if (!shape || user.Length == 0 || stored.Length != 64 || !matches)
                {
                    await AuditMethods.AppendAsync(connection, transaction, context, "auth.password", "denied", "Session", "", "invalid_login", token).ConfigureAwait(false);
                    return;
                }
                string id = Ids.New("ses_"); DateTime expiry = DateTime.UtcNow.AddHours(1);
                issued = _Protector.Protect(new SessionClaims { SessionId = id, TenantId = tenant, UserId = user, IssuedUtc = DateTime.UtcNow, ExpiresUtc = expiry, TokenId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) });
                await ExecuteAsync(connection, transaction, "INSERT INTO auth_sessions(id,tenant_id,user_id,token_hash,expires_utc,created_utc,updated_utc) VALUES(@id,@tenant,@user,@hash,@expires,@now,@now)", token, P("id", id, "tenant", tenant, "user", user, "hash", Hash(issued), "expires", expiry.ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false);
                context.IsAuthenticated = true; context.TenantId = tenant; context.UserId = user; context.SessionId = id; context.AuthScheme = "PasswordExchange";
                await AuditMethods.AppendAsync(connection, transaction, context, "session.issue", "issued", "Session", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            if (issued.Length == 0) throw new DomainException(401, "invalid_login", "The tenant, identity or password is invalid or inactive.");
            return issued;
        }
        /// <summary>Resolve an active session and its current active user and tenant.</summary>
        public async Task<RequestContext?> AuthenticateAsync(string secret, CancellationToken token = default)
        {
            SessionClaims? claims = _Protector.Unprotect(secret);
            if (claims == null) return null;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT s.tenant_id,s.user_id,u.role,u.is_admin,u.is_tenant_admin FROM auth_sessions s JOIN users u ON u.id=s.user_id AND u.tenant_id=s.tenant_id JOIN tenants t ON t.id=s.tenant_id WHERE s.token_hash=@hash AND s.id=@session AND s.tenant_id=@tenant AND s.user_id=@user AND s.active=1 AND u.active=1 AND t.active=1 AND s.expires_utc>@now", P("hash", Hash(secret), "session", claims.SessionId, "tenant", claims.TenantId, "user", claims.UserId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                string role = S(reader, "role");
                return new RequestContext { SessionId = claims.SessionId, TenantId = S(reader, "tenant_id"), UserId = S(reader, "user_id"), Role = role, IsAuthenticated = true, IsAdmin = Convert.ToInt32(reader["is_admin"], CultureInfo.InvariantCulture) != 0, IsTenantAdmin = Convert.ToInt32(reader["is_tenant_admin"], CultureInfo.InvariantCulture) != 0 };
            }
        }
        /// <summary>Revoke a session immediately.</summary>
        public Task RevokeAsync(string secret, CancellationToken token = default)
        {
            return _Database.WriteAsync(async (connection, transaction) =>
            {
                SessionClaims? claims = _Protector.Unprotect(secret);
                int changed = await ExecuteAsync(connection, transaction, "UPDATE auth_sessions SET active=0,updated_utc=@now WHERE token_hash=@hash AND active=1", token, P("hash", Hash(secret))).ConfigureAwait(false);
                if (changed != 0 && claims != null) await AuditMethods.AppendAsync(connection, transaction, new RequestContext { IsAuthenticated = true, TenantId = claims.TenantId, UserId = claims.UserId, SessionId = claims.SessionId }, "session.revoke", "revoked", "Session", claims.SessionId, "", token).ConfigureAwait(false);
            }, token);
        }
        private static string Hash(string value) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))); }
    }
}
