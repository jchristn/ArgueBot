namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Backups;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Tenant, user and bearer-token credential storage with bypass-flag authorization and cascading deletes.</summary>
    public sealed class IdentityMethods : IIdentityMethods
    {
        private static readonly Regex EmailPattern = new Regex(@"\A[^\s@]+@[^\s@]+\z", RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;
        internal IdentityMethods(DatabaseDriverBase database) { _Database = database; }

        /// <inheritdoc />
        public async Task<List<Tenant>> TenantsAsync(RequestContext context, CancellationToken token = default)
        {
            RequireSystemAdmin(context);
            List<Tenant> result = new List<Tenant>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM tenants ORDER BY name,id"))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(ReadTenant(reader)); }
            return result;
        }

        /// <inheritdoc />
        public async Task<Tenant> TenantAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            if (!context.IsAdmin && id != context.TenantId) throw NotFound("tenant_not_found", "Tenant not found.");
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM tenants WHERE id=@id", P("id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadTenant(reader) : throw NotFound("tenant_not_found", "Tenant not found.");
        }

        /// <inheritdoc />
        public async Task<Tenant> CreateTenantAsync(RequestContext context, TenantRequest request, CancellationToken token = default)
        {
            RequireSystemAdmin(context); ArgumentNullException.ThrowIfNull(request);
            string name = Name(request.Name, "name", "tenant");
            string id = Ids.New("ten_");
            await _Database.WriteAsync((connection, transaction) => ExecuteAsync(connection, transaction, "INSERT INTO tenants(id,name,protected,revision,active,created_utc,updated_utc) VALUES(@id,@name,0,1,@active,@now,@now)", token, P("id", id, "name", name, "active", request.Active ? 1 : 0)), token).ConfigureAwait(false);
            return await TenantAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Tenant> UpdateTenantAsync(RequestContext context, string id, TenantRequest request, CancellationToken token = default)
        {
            RequireSystemAdmin(context); ArgumentNullException.ThrowIfNull(request);
            Tenant current = await TenantAsync(context, id, token).ConfigureAwait(false);
            if (current.Protected && !request.Active) throw Protected("The default tenant cannot be disabled.");
            string name = Name(request.Name, "name", "tenant");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await ExecuteAsync(connection, transaction, "UPDATE tenants SET name=@name,active=@active,revision=revision+1,updated_utc=@now WHERE id=@id AND revision=@revision", token, P("id", id, "name", name, "active", request.Active ? 1 : 0, "revision", request.Revision)).ConfigureAwait(false) == 0) throw Changed();
            }, token).ConfigureAwait(false);
            return await TenantAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteTenantAsync(RequestContext context, string id, string confirmName, CancellationToken token = default)
        {
            RequireSystemAdmin(context);
            Tenant current = await TenantAsync(context, id, token).ConfigureAwait(false);
            if (current.Protected) throw Protected("The default tenant cannot be deleted.");
            if (id == context.TenantId) throw new DomainException(409, "own_tenant", "You cannot delete the tenant you are signed in to.");
            if (!string.Equals(confirmName, current.Name, StringComparison.Ordinal)) throw new DomainException(400, "confirmation_mismatch", "Type the tenant's exact name to confirm deletion.", "confirmName");
            // Delete child rows first: every table with a tenant_id column, newest tables first, then the tenant itself.
            List<string> tables = BackupSchema.Tables().Where(table => table.Key != "tenants" && table.Value.ContainsKey("tenant_id")).Select(table => table.Key).Reverse().ToList();
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                foreach (string table in tables) await ExecuteAsync(connection, transaction, "DELETE FROM " + table + " WHERE tenant_id=@tenant", token, P("tenant", id)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM tenants WHERE id=@tenant", token, P("tenant", id)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<User>> UsersAsync(RequestContext context, CancellationToken token = default)
        {
            RequireTenantAdmin(context);
            List<User> result = new List<User>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM users WHERE tenant_id=@tenant ORDER BY lower(email),id", P("tenant", context.TenantId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(ReadUser(reader)); }
            return result;
        }

        /// <inheritdoc />
        public async Task<User> UserAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            if (id != context.UserId && !IsTenantAdmin(context)) throw NotFound("user_not_found", "User not found.");
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM users WHERE tenant_id=@tenant AND id=@id", P("tenant", context.TenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadUser(reader) : throw NotFound("user_not_found", "User not found.");
        }

        /// <inheritdoc />
        public async Task<User> CreateUserAsync(RequestContext context, UserRequest request, CancellationToken token = default)
        {
            RequireTenantAdmin(context); ArgumentNullException.ThrowIfNull(request);
            if (request.IsAdmin && !context.IsAdmin) throw Forbidden("Only a system administrator can create another system administrator.");
            string email = Email(request.Email);
            ValidatePassword(request.Password, "password");
            string first = Optional(request.FirstName, "firstName"); string last = Optional(request.LastName, "lastName");
            PasswordHasher.Create(request.Password, out string hash, out string salt, out int iterations);
            string id = Ids.New("usr_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await RequireUniqueEmailAsync(connection, transaction, context.TenantId, email, "", token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO users(id,tenant_id,name,first_name,last_name,email,password_sha256,password_salt,password_iterations,revision,role,is_admin,is_tenant_admin,protected,active,created_utc,updated_utc) VALUES(@id,@tenant,@name,@first,@last,@email,@hash,@salt,@iterations,1,'member',@admin,@tenantAdmin,0,@active,@now,@now)", token, P("id", id, "tenant", context.TenantId, "name", (first + " " + last).Trim(), "first", first, "last", last, "email", email, "hash", hash, "salt", salt, "iterations", iterations, "admin", request.IsAdmin ? 1 : 0, "tenantAdmin", request.IsTenantAdmin || request.IsAdmin ? 1 : 0, "active", request.Active ? 1 : 0)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await UserAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<User> UpdateUserAsync(RequestContext context, string id, UserRequest request, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(request);
            User current = await UserAsync(context, id, token).ConfigureAwait(false);
            bool admin = IsTenantAdmin(context);
            bool flagsChanged = request.IsAdmin != current.IsAdmin || request.IsTenantAdmin != current.IsTenantAdmin || request.Active != current.Active;
            if (flagsChanged && !admin) throw Forbidden("Only a tenant administrator can change administrator flags or enable and disable users.");
            if (request.IsAdmin != current.IsAdmin && !context.IsAdmin) throw Forbidden("Only a system administrator can grant or remove system administration.");
            if (current.Protected && (!request.Active || !request.IsAdmin || !request.IsTenantAdmin)) throw Protected("The default administrator cannot be disabled or lose administrator access.");
            if (id == context.UserId && flagsChanged && (!request.Active || (current.IsTenantAdmin && !request.IsTenantAdmin))) throw new DomainException(409, "own_access", "You cannot disable yourself or remove your own administrator access.");
            string email = Email(request.Email);
            string first = Optional(request.FirstName, "firstName"); string last = Optional(request.LastName, "lastName");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await RequireUniqueEmailAsync(connection, transaction, context.TenantId, email, id, token).ConfigureAwait(false);
                if (await ExecuteAsync(connection, transaction, "UPDATE users SET name=@name,first_name=@first,last_name=@last,email=@email,is_admin=@admin,is_tenant_admin=@tenantAdmin,active=@active,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision", token, P("tenant", context.TenantId, "id", id, "name", (first + " " + last).Trim(), "first", first, "last", last, "email", email, "admin", request.IsAdmin ? 1 : 0, "tenantAdmin", request.IsTenantAdmin || request.IsAdmin ? 1 : 0, "active", request.Active ? 1 : 0, "revision", request.Revision)).ConfigureAwait(false) == 0) throw Changed();
                if (!request.Active) await RevokeSessionsAsync(connection, transaction, context.TenantId, id, "", "User disabled", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await UserAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteUserAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireTenantAdmin(context);
            User current = await UserAsync(context, id, token).ConfigureAwait(false);
            if (current.Protected) throw Protected("The default administrator cannot be deleted.");
            if (id == context.UserId) throw new DomainException(409, "own_access", "You cannot delete yourself.");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Dictionary<string, object?> values = P("tenant", context.TenantId, "user", id, "heir", context.UserId);
                // Data is shared per tenant: keep the user's conversations, agents and receipts, credited to the administrator deleting them.
                foreach (string table in new[] { "conversations", "profiles", "command_receipts" }) await ExecuteAsync(connection, transaction, "UPDATE " + table + " SET user_id=@heir WHERE tenant_id=@tenant AND user_id=@user", token, values).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE conversation_shares SET created_by_user_id=@heir WHERE tenant_id=@tenant AND created_by_user_id=@user", token, values).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM authorization_user_assignments WHERE tenant_id=@tenant AND user_id=@user", token, values).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM auth_sessions WHERE tenant_id=@tenant AND user_id=@user", token, values).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM auth_credentials WHERE tenant_id=@tenant AND user_id=@user", token, values).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM users WHERE tenant_id=@tenant AND id=@user", token, values).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetPasswordAsync(RequestContext context, string id, PasswordChangeRequest request, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(request);
            bool own = id == context.UserId;
            if (!own && !IsTenantAdmin(context)) throw NotFound("user_not_found", "User not found.");
            ValidatePassword(request.NewPassword, "newPassword");
            string hash = ""; string salt = ""; int iterations = 0;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT password_sha256,password_salt,password_iterations FROM users WHERE tenant_id=@tenant AND id=@id", P("tenant", context.TenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw NotFound("user_not_found", "User not found.");
                hash = S(reader, "password_sha256"); salt = S(reader, "password_salt"); iterations = Convert.ToInt32(reader["password_iterations"], CultureInfo.InvariantCulture);
            }
            if (own && !PasswordHasher.Verify(request.CurrentPassword, hash, salt, iterations)) throw new DomainException(400, "invalid_password", "The current password is not correct.", "currentPassword");
            PasswordHasher.Create(request.NewPassword, out string newHash, out string newSalt, out int newIterations);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE users SET password_sha256=@hash,password_salt=@salt,password_iterations=@iterations,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", id, "hash", newHash, "salt", newSalt, "iterations", newIterations)).ConfigureAwait(false);
                await RevokeSessionsAsync(connection, transaction, context.TenantId, id, own ? context.SessionId : "", "Password changed", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AuthCredential>> CredentialsAsync(RequestContext context, string userId, CancellationToken token = default)
        {
            await RequireCredentialOwnerAsync(context, userId, token).ConfigureAwait(false);
            List<AuthCredential> result = new List<AuthCredential>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM auth_credentials WHERE tenant_id=@tenant AND user_id=@user ORDER BY created_utc,id", P("tenant", context.TenantId, "user", userId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(ReadCredential(reader)); }
            return result;
        }

        /// <inheritdoc />
        public async Task<AuthCredentialCreated> CreateCredentialAsync(RequestContext context, string userId, AuthCredentialRequest request, CancellationToken token = default)
        {
            await RequireCredentialOwnerAsync(context, userId, token).ConfigureAwait(false); ArgumentNullException.ThrowIfNull(request);
            string name = Name(request.Name, "name", "credential");
            if (request.ExpiresUtc != null && request.ExpiresUtc.Value.ToUniversalTime() <= DateTime.UtcNow) throw new DomainException(400, "invalid_expiry", "Choose an expiry in the future, or none.", "expiresUtc");
            string raw = SecretTokens.Create("abt_");
            string id = Ids.New("cred_");
            await _Database.WriteAsync((connection, transaction) => ExecuteAsync(connection, transaction, "INSERT INTO auth_credentials(id,tenant_id,user_id,name,token_sha256,token_last4,auth_mode,active,protected,expires_utc,revision,created_utc,updated_utc) VALUES(@id,@tenant,@user,@name,@hash,@last4,'DirectHeader',1,0,@expires,1,@now,@now)", token, P("id", id, "tenant", context.TenantId, "user", userId, "name", name, "hash", SecretTokens.Hash(raw), "last4", SecretTokens.Last4(raw), "expires", request.ExpiresUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))), token).ConfigureAwait(false);
            AuthCredential created = (await CredentialsAsync(context, userId, token).ConfigureAwait(false)).Single(item => item.Id == id);
            return new AuthCredentialCreated { Credential = created, Token = raw };
        }

        /// <inheritdoc />
        public async Task<AuthCredential> UpdateCredentialAsync(RequestContext context, string userId, string id, AuthCredentialRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            AuthCredential current = (await CredentialsAsync(context, userId, token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == id) ?? throw NotFound("credential_not_found", "Credential not found.");
            if (current.Protected && !request.Active) throw Protected("The default admin token cannot be disabled.");
            string name = Name(request.Name, "name", "credential");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await ExecuteAsync(connection, transaction, "UPDATE auth_credentials SET name=@name,active=@active,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND user_id=@user AND id=@id AND revision=@revision", token, P("tenant", context.TenantId, "user", userId, "id", id, "name", name, "active", request.Active ? 1 : 0, "revision", request.Revision)).ConfigureAwait(false) == 0) throw Changed();
                if (!request.Active) await RevokeSessionsAsync(connection, transaction, context.TenantId, userId, "", "Credential disabled", token, id).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return (await CredentialsAsync(context, userId, token).ConfigureAwait(false)).Single(item => item.Id == id);
        }

        /// <inheritdoc />
        public async Task DeleteCredentialAsync(RequestContext context, string userId, string id, CancellationToken token = default)
        {
            AuthCredential current = (await CredentialsAsync(context, userId, token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == id) ?? throw NotFound("credential_not_found", "Credential not found.");
            if (current.Protected) throw Protected("The default admin token cannot be deleted.");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "DELETE FROM auth_sessions WHERE tenant_id=@tenant AND credential_id=@id", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM auth_credentials WHERE tenant_id=@tenant AND user_id=@user AND id=@id", token, P("tenant", context.TenantId, "user", userId, "id", id)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <summary>Revoke a user's active sessions, optionally keeping one and optionally only those created by one credential.</summary>
        internal static Task<int> RevokeSessionsAsync(DbConnection connection, DbTransaction transaction, string tenantId, string userId, string keepSessionId, string reason, CancellationToken token, string credentialId = "")
        {
            return ExecuteAsync(connection, transaction, "UPDATE auth_sessions SET active=0,revoked_utc=@now,revocation_reason=@reason,updated_utc=@now WHERE tenant_id=@tenant AND user_id=@user AND active=1 AND id<>@keep AND (@credential='' OR credential_id=@credential)", token, P("tenant", tenantId, "user", userId, "keep", keepSessionId, "reason", reason, "credential", credentialId));
        }

        private async Task RequireCredentialOwnerAsync(RequestContext context, string userId, CancellationToken token)
        {
            RequireAuthenticated(context);
            if (userId != context.UserId && !IsTenantAdmin(context)) throw NotFound("user_not_found", "User not found.");
            await UserAsync(context, userId, token).ConfigureAwait(false);
        }
        private static async Task RequireUniqueEmailAsync(DbConnection connection, DbTransaction transaction, string tenantId, string email, string exceptId, CancellationToken token)
        {
            object? existing = await ScalarAsync(connection, transaction, "SELECT id FROM users WHERE tenant_id=@tenant AND lower(email)=lower(@email) AND id<>@except", token, P("tenant", tenantId, "email", email, "except", exceptId)).ConfigureAwait(false);
            if (existing != null) throw new DomainException(409, "duplicate_email", "Another user in this tenant already uses that email address.", "email");
        }
        private static bool IsTenantAdmin(RequestContext context) { return context.IsAdmin || context.IsTenantAdmin; }
        private static void RequireTenantAdmin(RequestContext context) { RequireAuthenticated(context); if (!IsTenantAdmin(context)) throw Forbidden("This action needs a tenant administrator."); }
        private static void RequireSystemAdmin(RequestContext context) { RequireAuthenticated(context); if (!context.IsAdmin) throw Forbidden("This action needs a system administrator."); }
        private static string Name(string value, string field, string label)
        {
            string trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0 || trimmed.Length > 200 || trimmed.Any(char.IsControl)) throw new DomainException(400, "invalid_" + label, "Enter a " + label + " name of 1 through 200 characters.", field);
            return trimmed;
        }
        private static string Optional(string value, string field)
        {
            string trimmed = (value ?? "").Trim();
            if (trimmed.Length > 200 || trimmed.Any(char.IsControl)) throw new DomainException(400, "invalid_user", "Names can be at most 200 characters.", field);
            return trimmed;
        }
        private static string Email(string value)
        {
            string trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0 || trimmed.Length > 320 || !EmailPattern.IsMatch(trimmed)) throw new DomainException(400, "invalid_email", "Enter an email address such as name@example.com.", "email");
            return trimmed;
        }
        private static void ValidatePassword(string value, string field)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 8 || value.Length > 256) throw new DomainException(400, "invalid_password", "Use a password of 8 through 256 characters.", field);
        }
        private static DomainException NotFound(string code, string message) { return new DomainException(404, code, message); }
        private static DomainException Forbidden(string message) { return new DomainException(403, "forbidden", message); }
        private static DomainException Protected(string message) { return new DomainException(409, "protected_record", message); }
        private static DomainException Changed() { return new DomainException(409, "revision_conflict", "This record changed. Reload it before saving."); }
        private static DateTime Time(DbDataReader reader, string column) { return DateTime.Parse(S(reader, column), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
        private static DateTime? OptionalTime(DbDataReader reader, string column) { string? value = N(reader, column); return string.IsNullOrEmpty(value) ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
        private static bool Flag(DbDataReader reader, string column) { return Convert.ToInt64(reader[column], CultureInfo.InvariantCulture) != 0; }
        private static Tenant ReadTenant(DbDataReader reader)
        {
            return new Tenant { Id = S(reader, "id"), Name = S(reader, "name"), Active = Flag(reader, "active"), Protected = Flag(reader, "protected"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = Time(reader, "created_utc"), UpdatedUtc = Time(reader, "updated_utc") };
        }
        private static User ReadUser(DbDataReader reader)
        {
            return new User { Id = S(reader, "id"), TenantId = S(reader, "tenant_id"), FirstName = S(reader, "first_name"), LastName = S(reader, "last_name"), Email = S(reader, "email"), IsAdmin = Flag(reader, "is_admin"), IsTenantAdmin = Flag(reader, "is_tenant_admin"), Active = Flag(reader, "active"), Protected = Flag(reader, "protected"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = Time(reader, "created_utc"), UpdatedUtc = Time(reader, "updated_utc") };
        }
        private static AuthCredential ReadCredential(DbDataReader reader)
        {
            return new AuthCredential { Id = S(reader, "id"), TenantId = S(reader, "tenant_id"), UserId = S(reader, "user_id"), Name = S(reader, "name"), TokenLast4 = S(reader, "token_last4"), AuthMode = S(reader, "auth_mode"), Active = Flag(reader, "active"), Protected = Flag(reader, "protected"), ExpiresUtc = OptionalTime(reader, "expires_utc"), LastUsedUtc = OptionalTime(reader, "last_used_utc"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = Time(reader, "created_utc"), UpdatedUtc = Time(reader, "updated_utc") };
        }
    }
}
