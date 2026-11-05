namespace ArgueBot.Core.Database
{
    using ArgueBot.Core.Services;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>
    /// Ensures the default tenant, the default administrator (admin@arguebot / password) and its bearer token
    /// (arguebotadmin). Each value is set only when missing, so later changes are never reset.
    /// </summary>
    public static class IdentitySeed
    {
        /// <summary>The default tenant, which also holds every record created before schema 30.</summary>
        public const string TenantId = "ten_local";
        /// <summary>The default administrator.</summary>
        public const string UserId = "usr_local";
        /// <summary>The default administrator's bearer-token credential.</summary>
        public const string CredentialId = "cred_default_admin";
        /// <summary>Default sign-in email.</summary>
        public const string Email = "admin@arguebot";
        /// <summary>Default sign-in password.</summary>
        public const string Password = "password";
        /// <summary>Default bearer token.</summary>
        public const string Token = "arguebotadmin";

        internal static async Task EnsureAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
        {
            await ExecuteAsync(connection, transaction, "UPDATE tenants SET name='Default tenant',updated_utc=@now WHERE id=@tenant AND name='Local'", token, P("tenant", TenantId)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE tenants SET protected=1 WHERE id=@tenant AND protected=0", token, P("tenant", TenantId)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE users SET email=@email,updated_utc=@now WHERE id=@user AND email=''", token, P("user", UserId, "email", Email)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE users SET first_name='Default',last_name='Admin',name='Default Admin',updated_utc=@now WHERE id=@user AND (first_name='' OR first_name='Local owner')", token, P("user", UserId)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE users SET protected=1,is_admin=1,is_tenant_admin=1 WHERE id=@user AND (protected=0 OR is_admin=0 OR is_tenant_admin=0)", token, P("user", UserId)).ConfigureAwait(false);
            object? salt = await ScalarAsync(connection, transaction, "SELECT password_salt FROM users WHERE id=@user", token, P("user", UserId)).ConfigureAwait(false);
            if (salt is string existing && existing.Length == 0)
            {
                PasswordHasher.Create(Password, out string hash, out string newSalt, out int iterations);
                await ExecuteAsync(connection, transaction, "UPDATE users SET password_sha256=@hash,password_salt=@salt,password_iterations=@iterations,updated_utc=@now WHERE id=@user AND password_salt=''", token, P("user", UserId, "hash", hash, "salt", newSalt, "iterations", iterations)).ConfigureAwait(false);
            }
            object? credential = await ScalarAsync(connection, transaction, "SELECT id FROM auth_credentials WHERE id=@id", token, P("id", CredentialId)).ConfigureAwait(false);
            if (credential == null)
                await ExecuteAsync(connection, transaction, "INSERT INTO auth_credentials(id,tenant_id,user_id,name,token_sha256,token_last4,auth_mode,active,protected,revision,created_utc,updated_utc) VALUES(@id,@tenant,@user,'Default admin token',@hash,@last4,'DirectHeader',1,1,1,@now,@now)", token, P("id", CredentialId, "tenant", TenantId, "user", UserId, "hash", SecretTokens.Hash(Token), "last4", SecretTokens.Last4(Token))).ConfigureAwait(false);
        }
    }
}
