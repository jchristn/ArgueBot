namespace ArgueBot.Core.Database
{
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal static class PasswordVerifierMigration
    {
        internal static async Task ApplyAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
        {
            string cursor = "";
            while (true)
            {
                List<LegacyPasswordRow> rows = new List<LegacyPasswordRow>();
                using (DbCommand command = Command(connection, transaction, "SELECT id,tenant_id,password_sha256 FROM users WHERE id>@cursor AND password_sha256<>'' AND password_iterations=0 ORDER BY id LIMIT 100", P("cursor", cursor)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) { cursor = S(reader, "id"); rows.Add(new LegacyPasswordRow { Id = cursor, TenantId = S(reader, "tenant_id"), Equivalent = S(reader, "password_sha256") }); } }
                foreach (LegacyPasswordRow row in rows)
                {
                    token.ThrowIfCancellationRequested(); LegacyPasswordVerifier verifier = LegacyPasswordVerifier.Create(row.Equivalent);
                    await ExecuteAsync(connection, transaction, "UPDATE users SET password_sha256=@hash,password_salt=@salt,password_iterations=@iterations,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND password_iterations=0", token, P("hash", verifier.Hash, "salt", verifier.Salt, "iterations", verifier.Iterations, "tenant", row.TenantId, "id", row.Id)).ConfigureAwait(false);
                }
                if (rows.Count < 100) return;
            }
        }
    }
}
