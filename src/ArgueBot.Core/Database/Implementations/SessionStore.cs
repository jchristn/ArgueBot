namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Service-internal reads and writes behind sign-in and bearer-token validation. These run before a request context exists.</summary>
    internal static class SessionStore
    {
        internal static async Task<List<LoginCandidate>> LoginCandidatesAsync(DatabaseDriverBase database, string email, string tenantId, CancellationToken token)
        {
            List<LoginCandidate> result = new List<LoginCandidate>();
            using (DbConnection connection = await database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT u.*,t.name AS tenant_name,t.active AS tenant_active,t.protected AS tenant_protected,t.revision AS tenant_revision,t.created_utc AS tenant_created_utc,t.updated_utc AS tenant_updated_utc FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE u.email<>'' AND lower(u.email)=lower(@email) AND (@tenant='' OR u.tenant_id=@tenant) ORDER BY t.name,t.id", P("email", email, "tenant", tenantId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    result.Add(new LoginCandidate { User = IdentityMethods.ReadUser(reader), Tenant = Tenant(reader), Hash = S(reader, "password_sha256"), Salt = S(reader, "password_salt"), Iterations = Convert.ToInt32(reader["password_iterations"], CultureInfo.InvariantCulture) });
            }
            return result;
        }

        internal static async Task<StoredPrincipal?> SessionAsync(DatabaseDriverBase database, string sessionId, CancellationToken token)
        {
            using (DbConnection connection = await database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT u.*,s.id AS principal_id,s.credential_id AS principal_credential,s.principal_type AS principal_kind,s.token_hash AS principal_hash,s.active AS principal_active,s.revoked_utc AS principal_revoked,s.expires_utc AS principal_expires,s.last_used_utc AS principal_used,COALESCE(c.active,1) AS credential_active,t.name AS tenant_name,t.active AS tenant_active,t.protected AS tenant_protected,t.revision AS tenant_revision,t.created_utc AS tenant_created_utc,t.updated_utc AS tenant_updated_utc FROM auth_sessions s JOIN users u ON u.tenant_id=s.tenant_id AND u.id=s.user_id JOIN tenants t ON t.id=s.tenant_id LEFT JOIN auth_credentials c ON c.tenant_id=s.tenant_id AND c.id=s.credential_id WHERE s.id=@id", P("id", sessionId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                return new StoredPrincipal { User = IdentityMethods.ReadUser(reader), Tenant = Tenant(reader), Id = S(reader, "principal_id"), CredentialId = S(reader, "principal_credential"), PrincipalType = S(reader, "principal_kind"), TokenHash = S(reader, "principal_hash"), Active = IdentityMethods.Flag(reader, "principal_active") && N(reader, "principal_revoked") == null, CredentialActive = IdentityMethods.Flag(reader, "credential_active"), ExpiresUtc = IdentityMethods.OptionalTime(reader, "principal_expires"), LastUsedUtc = IdentityMethods.OptionalTime(reader, "principal_used") };
            }
        }

        internal static async Task<StoredPrincipal?> CredentialAsync(DatabaseDriverBase database, string tokenHash, CancellationToken token)
        {
            using (DbConnection connection = await database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT u.*,c.id AS principal_id,c.token_sha256 AS principal_hash,c.active AS principal_active,c.expires_utc AS principal_expires,c.last_used_utc AS principal_used,t.name AS tenant_name,t.active AS tenant_active,t.protected AS tenant_protected,t.revision AS tenant_revision,t.created_utc AS tenant_created_utc,t.updated_utc AS tenant_updated_utc FROM auth_credentials c JOIN users u ON u.tenant_id=c.tenant_id AND u.id=c.user_id JOIN tenants t ON t.id=c.tenant_id WHERE c.token_sha256=@hash", P("hash", tokenHash)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                string id = S(reader, "principal_id");
                return new StoredPrincipal { User = IdentityMethods.ReadUser(reader), Tenant = Tenant(reader), Id = id, CredentialId = id, PrincipalType = "Credential", TokenHash = S(reader, "principal_hash"), Active = IdentityMethods.Flag(reader, "principal_active"), ExpiresUtc = IdentityMethods.OptionalTime(reader, "principal_expires"), LastUsedUtc = IdentityMethods.OptionalTime(reader, "principal_used") };
            }
        }

        internal static Task IssueAsync(DatabaseDriverBase database, string id, User user, string credentialId, string principalType, string scheme, string tokenId, string tokenHash, string sourceIp, string userAgent, DateTime expiresUtc, CancellationToken token)
        {
            return database.WriteAsync((connection, transaction) => ExecuteAsync(connection, transaction, "INSERT INTO auth_sessions(id,tenant_id,user_id,token_hash,credential_id,principal_type,auth_scheme,token_id,source_ip,user_agent,last_used_utc,expires_utc,active,protected,created_utc,updated_utc) VALUES(@id,@tenant,@user,@hash,@credential,@principal,@scheme,@jti,@ip,@agent,@now,@expires,1,0,@now,@now)", token,
                P("id", id, "tenant", user.TenantId, "user", user.Id, "hash", tokenHash, "credential", credentialId, "principal", principalType, "scheme", scheme, "jti", tokenId, "ip", Cut(sourceIp, 64), "agent", Cut(userAgent, 256), "expires", Stamp(expiresUtc))), token);
        }

        internal static async Task<int> ReplaceTokenAsync(DatabaseDriverBase database, string id, string oldHash, string tokenId, string tokenHash, DateTime expiresUtc, CancellationToken token)
        {
            int changed = 0;
            await database.WriteAsync(async (connection, transaction) => changed = await ExecuteAsync(connection, transaction, "UPDATE auth_sessions SET token_hash=@hash,token_id=@jti,expires_utc=@expires,last_used_utc=@now,updated_utc=@now WHERE id=@id AND token_hash=@old AND active=1 AND revoked_utc IS NULL", token, P("id", id, "old", oldHash, "hash", tokenHash, "jti", tokenId, "expires", Stamp(expiresUtc))).ConfigureAwait(false), token).ConfigureAwait(false);
            return changed;
        }

        internal static Task TouchSessionAsync(DatabaseDriverBase database, string id, CancellationToken token)
        { return database.WriteAsync((connection, transaction) => ExecuteAsync(connection, transaction, "UPDATE auth_sessions SET last_used_utc=@now WHERE id=@id", token, P("id", id)), token); }

        internal static Task TouchCredentialAsync(DatabaseDriverBase database, string id, CancellationToken token)
        { return database.WriteAsync((connection, transaction) => ExecuteAsync(connection, transaction, "UPDATE auth_credentials SET last_used_utc=@now WHERE id=@id", token, P("id", id)), token); }

        internal static Task RevokeAsync(DatabaseDriverBase database, string id, string reason, CancellationToken token)
        { return database.WriteAsync((connection, transaction) => ExecuteAsync(connection, transaction, "UPDATE auth_sessions SET active=0,revoked_utc=@now,revocation_reason=@reason,updated_utc=@now WHERE id=@id AND active=1", token, P("id", id, "reason", reason)), token); }

        internal static async Task<int> ActiveTenantCountAsync(DatabaseDriverBase database, CancellationToken token)
        {
            using (DbConnection connection = await database.OpenAsync(token).ConfigureAwait(false))
                return Convert.ToInt32(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM tenants WHERE active=1", token).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        internal static string Stamp(DateTime value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
        private static string Cut(string value, int length) { return value.Length <= length ? value : value.Substring(0, length); }
        private static Tenant Tenant(DbDataReader reader)
        {
            return new Tenant { Id = S(reader, "tenant_id"), Name = S(reader, "tenant_name"), Active = IdentityMethods.Flag(reader, "tenant_active"), Protected = IdentityMethods.Flag(reader, "tenant_protected"), Revision = Convert.ToInt64(reader["tenant_revision"], CultureInfo.InvariantCulture), CreatedUtc = IdentityMethods.Time(reader, "tenant_created_utc"), UpdatedUtc = IdentityMethods.Time(reader, "tenant_updated_utc") };
        }
    }
}
