namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
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
