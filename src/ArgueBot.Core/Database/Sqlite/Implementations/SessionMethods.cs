namespace ArgueBot.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using static ArgueBot.Core.Database.Sqlite.SqliteDatabaseDriver;

    /// <summary>Revocable opaque local sessions. Only token digests are persisted.</summary>
    public sealed class SessionMethods
    {
        private readonly SqliteDatabaseDriver _Database;
        /// <summary>Create a session store using the shared database.</summary>
        public SessionMethods(SqliteDatabaseDriver database) { _Database = database; }
        /// <summary>Issue a twelve-hour local session after successful protected enrollment.</summary>
        public async Task<string> IssueLocalAsync(CancellationToken token = default)
        {
            string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO auth_sessions(id,tenant_id,user_id,token_hash,expires_utc,created_utc,updated_utc) VALUES(@id,'ten_local','usr_local',@hash,@expires,@now,@now)", token, P("id", Ids.New("ses_"), "hash", Hash(secret), "expires", DateTime.UtcNow.AddHours(12).ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return secret;
        }
        /// <summary>Resolve an active session and its current active user and tenant.</summary>
        public async Task<RequestContext?> AuthenticateAsync(string secret, CancellationToken token = default)
        {
            if (secret.Length != 64) return null;
            using (SqliteConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (SqliteCommand command = Command(connection, null, "SELECT s.tenant_id,s.user_id,u.role FROM auth_sessions s JOIN users u ON u.id=s.user_id AND u.tenant_id=s.tenant_id JOIN tenants t ON t.id=s.tenant_id WHERE s.token_hash=@hash AND s.active=1 AND u.active=1 AND t.active=1 AND s.expires_utc>@now", P("hash", Hash(secret))))
            using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                string role = S(reader, "role");
                return new RequestContext { TenantId = S(reader, "tenant_id"), UserId = S(reader, "user_id"), Role = role, IsAuthenticated = true, IsAdmin = role == "admin", IsTenantAdmin = role == "admin" };
            }
        }
        /// <summary>Revoke a session immediately.</summary>
        public Task RevokeAsync(string secret, CancellationToken token = default)
        {
            return _Database.WriteAsync(async (connection, transaction) => { await ExecuteAsync(connection, transaction, "UPDATE auth_sessions SET active=0,updated_utc=@now WHERE token_hash=@hash", token, P("hash", Hash(secret))).ConfigureAwait(false); }, token);
        }
        private static string Hash(string value) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))); }
    }
}
