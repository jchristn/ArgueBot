namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Net.Mail;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Tenant identity management. Global administrative flags never follow role names.</summary>
    public sealed class UserMethods : IUserMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal UserMethods(DatabaseDriverBase database) { _Database = database; }
        /// <inheritdoc />
        public async Task<UserPage> EnumerateAsync(RequestContext context, string search = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            RequireAdmin(context);
            if (search == null || search.Length > 200 || pageNumber < 1 || pageSize < 1 || pageSize > 100) throw new DomainException(400, "invalid_filter", "Use a bounded search and a positive page with size one through one hundred.");
            UserPage page = new UserPage { PageNumber = pageNumber, PageSize = pageSize };
            const string scope = "tenant_id=@tenant AND (instr(lower(name),lower(@search))>0 OR instr(lower(email),lower(@search))>0)";
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM users WHERE " + scope, token, P("tenant", context.TenantId, "search", search)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM users WHERE " + scope + " ORDER BY name,id LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "search", search, "limit", pageSize, "offset", checked((long)(pageNumber - 1) * pageSize))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) page.Items.Add(Read(reader)); }
            }
            return page;
        }
        /// <inheritdoc />
        public async Task<UserRecord> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            if (id != context.UserId) RequireAdmin(context);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM users WHERE tenant_id=@tenant AND id=@id", P("tenant", context.TenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "User not found."); return Read(reader); }
        }
        /// <inheritdoc />
        public async Task<UserRecord> CreateAsync(RequestContext context, UserRequest request, CancellationToken token = default)
        {
            RequireAdmin(context); Validate(context, request);
            string id = Ids.New("usr_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await CheckEmailAsync(connection, transaction, context.TenantId, request.Email, id, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO users(id,tenant_id,name,email,password_sha256,role,is_admin,is_tenant_admin,active,created_utc,updated_utc) VALUES(@id,@tenant,@name,@email,@password,@role,@admin,@tenantadmin,@active,@now,@now)", token, P("id", id, "tenant", context.TenantId, "name", request.Name.Trim(), "email", request.Email, "password", request.PasswordSha256?.ToUpperInvariant() ?? "", "role", request.Role, "admin", request.IsAdmin ? 1 : 0, "tenantadmin", request.IsTenantAdmin ? 1 : 0, "active", request.Active ? 1 : 0)).ConfigureAwait(false);
                await AuditMethods.AppendAsync(connection, transaction, context, "user.create", "created", "User", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }
        /// <inheritdoc />
        public async Task<UserRecord> UpdateAsync(RequestContext context, string id, UserRequest request, CancellationToken token = default)
        {
            RequireAdmin(context); Validate(context, request);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                UserRecord current;
                using (DbCommand command = Command(connection, transaction, "SELECT * FROM users WHERE tenant_id=@tenant AND id=@id", P("tenant", context.TenantId, "id", id)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "User not found."); current = Read(reader); }
                if (current.Protected) throw new DomainException(403, "protected_user", "Protected identities cannot be modified through normal administration.");
                if (!context.IsAdmin && current.IsAdmin) throw new DomainException(403, "forbidden", "Only a system administrator may modify system administrators.");
                if (current.Revision != request.ExpectedRevision) throw new DomainException(409, "revision_conflict", "The user changed. Reload before saving.");
                await CheckEmailAsync(connection, transaction, context.TenantId, request.Email, id, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE users SET name=@name,email=@email,password_sha256=CASE WHEN CAST(@password AS TEXT) IS NULL THEN password_sha256 ELSE CAST(@password AS TEXT) END,role=@role,is_admin=@admin,is_tenant_admin=@tenantadmin,active=@active,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("id", id, "tenant", context.TenantId, "name", request.Name.Trim(), "email", request.Email, "password", request.PasswordSha256?.ToUpperInvariant(), "role", request.Role, "admin", request.IsAdmin ? 1 : 0, "tenantadmin", request.IsTenantAdmin ? 1 : 0, "active", request.Active ? 1 : 0)).ConfigureAwait(false);
                if (!request.Active || request.PasswordSha256 != null)
                {
                    await ExecuteAsync(connection, transaction, "UPDATE auth_sessions SET active=0,updated_utc=@now WHERE tenant_id=@tenant AND user_id=@id AND active=1", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false);
                    await AuditMethods.AppendAsync(connection, transaction, context, "user.sessions_revoke", "revoked", "User", id, "", token).ConfigureAwait(false);
                }
                await AuditMethods.AppendAsync(connection, transaction, context, request.PasswordSha256 == null ? "user.update" : "user.password_change", "updated", "User", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }
        internal static void RequireAdmin(RequestContext context)
        { RequireAuthenticated(context); if (!context.IsAdmin && !context.IsTenantAdmin) throw new DomainException(403, "forbidden", "Identity administration requires tenant or system administrator permission."); }
        private static void Validate(RequestContext context, UserRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.Email == null || request.Email.Length > 254 || !MailAddress.TryCreate(request.Email, out MailAddress? email) || email.Address != request.Email || request.Role is not ("viewer" or "operator" or "admin")) throw new DomainException(400, "invalid_user", "Supply a display name, email address and a supported domain role.");
            request.Email = request.Email.ToLowerInvariant();
            if (request.IsAdmin && !context.IsAdmin) throw new DomainException(403, "forbidden", "Only a system administrator may grant system administrator privileges.");
            if (request.PasswordSha256 != null && !Regex.IsMatch(request.PasswordSha256, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)) throw new DomainException(400, "invalid_password", "Supply exactly sixty-four hexadecimal SHA-256 password-equivalent characters or omit the password.");
        }
        private static async Task CheckEmailAsync(DbConnection connection, DbTransaction transaction, string tenant, string email, string id, CancellationToken token)
        {
            long duplicates = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM users WHERE tenant_id=@tenant AND email=@email AND id<>@id", token, P("tenant", tenant, "email", email, "id", id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (duplicates != 0) throw new DomainException(409, "email_conflict", "That email address is already configured in this tenant.");
        }
        private static UserRecord Read(DbDataReader reader)
        {
            return new UserRecord { Id = S(reader, "id"), TenantId = S(reader, "tenant_id"), Name = S(reader, "name"), Email = S(reader, "email"), Role = S(reader, "role"), IsAdmin = Convert.ToInt32(reader["is_admin"], CultureInfo.InvariantCulture) != 0, IsTenantAdmin = Convert.ToInt32(reader["is_tenant_admin"], CultureInfo.InvariantCulture) != 0, Active = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0, Protected = Convert.ToInt32(reader["protected"], CultureInfo.InvariantCulture) != 0, HasPassword = S(reader, "password_sha256").Length > 0, Revision = Convert.ToInt32(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
        }
    }
}
