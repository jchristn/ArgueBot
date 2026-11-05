namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Write-only encrypted credentials; plaintext exists only in the execution service.</summary>
    public sealed class CredentialMethods : ICredentialMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal CredentialMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Read safe metadata without ciphertext or secret values.</summary>
        public async Task<List<CredentialMetadata>> EnumerateAsync(RequestContext context, CancellationToken token = default, string id = "", int pageNumber = 1, int pageSize = 100)
        {
            RequireAuthenticated(context); CatalogMethods.ValidatePage(pageNumber, pageSize); List<CredentialMetadata> result = new List<CredentialMetadata>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT id,name,kind,parameter_name,status,revision FROM connection_credentials WHERE tenant_id=@tenant AND (@id='' OR id=@id) ORDER BY name,id LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "id", id, "limit", pageSize, "offset", ((long)pageNumber - 1) * pageSize)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new CredentialMetadata { Id = S(reader, "id"), Name = S(reader, "name"), Kind = S(reader, "kind"), ParameterName = S(reader, "parameter_name"), Status = S(reader, "status"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture) }); }
            return result;
        }
        /// <inheritdoc />
        public async Task<long> CountOtherKeysAsync(RequestContext context, string keyId, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentException.ThrowIfNullOrEmpty(keyId);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM connection_credentials WHERE tenant_id=@tenant AND ciphertext LIKE 'v1:%' AND substr(ciphertext,1,@length)<>@prefix", token, P("tenant", context.TenantId, "length", keyId.Length + 4, "prefix", "v1:" + keyId + ":")).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        /// <inheritdoc />
        public async Task InspectAsync(Action<string, string, string, string> visit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(visit);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                string after = "";
                while (true)
                {
                    int read = 0;
                    using (DbCommand command = Command(connection, null, "SELECT id,name,status,ciphertext FROM connection_credentials WHERE id>@after ORDER BY id LIMIT 100", P("after", after)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) { read++; after = S(reader, "id"); visit(after, S(reader, "name"), S(reader, "status"), S(reader, "ciphertext")); } }
                    if (read < 100) return;
                }
            }
        }
        /// <inheritdoc />
        public async Task<long> CountNotUsingKeyAsync(string keyId, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(keyId);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM connection_credentials WHERE substr(ciphertext,1,@length)<>@prefix", token, P("length", keyId.Length + 4, "prefix", "v1:" + keyId + ":")).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }
        /// <summary>Encrypt and revision-check a service-owned credential write. Never expose this operation as a plaintext read API.</summary>
        public async Task<CredentialMetadata> SaveAsync(RequestContext context, string id, CredentialRequest request, Func<string, string, string> encrypt, CancellationToken token)
        {
            RequireWrite(context); bool create = id.Length == 0;
            if (create) id = Ids.New("sec_");
            string ciphertext = encrypt(id, request.SecretValue);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Dictionary<string, object?> values = P("id", id, "tenant", context.TenantId, "name", request.Name, "kind", request.Kind, "parameter", request.ParameterName, "ciphertext", ciphertext, "revision", request.Revision);
                if (create) await ExecuteAsync(connection, transaction, "INSERT INTO connection_credentials(id,tenant_id,name,kind,parameter_name,ciphertext,status,created_utc,updated_utc) VALUES(@id,@tenant,@name,@kind,@parameter,@ciphertext,'ready',@now,@now)", token, values).ConfigureAwait(false);
                else if (await ExecuteAsync(connection, transaction, "UPDATE connection_credentials SET name=@name,kind=@kind,ciphertext=@ciphertext,status='ready',revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision AND kind=@kind AND parameter_name=@parameter", token, values).ConfigureAwait(false) != 1) throw new DomainException(409, "credential_changed", "Refresh credential metadata before replacing its value. Credential kind and parameter name cannot change.");
            }, token).ConfigureAwait(false);
            return new CredentialMetadata { Id = id, Name = request.Name, Kind = request.Kind, ParameterName = request.ParameterName, Revision = create ? 1 : request.Revision + 1 };
        }
        /// <summary>Read ciphertext for service-owned decryption; never expose this value over HTTP.</summary>
        public async Task<string> ResolveAsync(RequestContext context, string id, CancellationToken token, Action<CredentialMetadata>? resolved = null)
        {
            RequireAuthenticated(context);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT ciphertext,id,name,kind,parameter_name,status,revision FROM connection_credentials WHERE tenant_id=@tenant AND id=@id AND status='ready'", P("tenant", context.TenantId, "id", id)))
            {
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(409, "credential_unavailable", "The credential is unavailable. Import a fresh login or enable its replacement before resuming.");
                    resolved?.Invoke(new CredentialMetadata { Id = id, Name = S(reader, "name"), Kind = S(reader, "kind"), ParameterName = S(reader, "parameter_name"), Status = S(reader, "status"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture) });
                    return S(reader, "ciphertext");
                }
            }
        }
        /// <summary>Claim exclusive refresh responsibility using the expected credential revision.</summary>
        public async Task<long> ClaimRefreshAsync(RequestContext context, string id, long revision, CancellationToken token)
        {
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await ExecuteAsync(connection, transaction, "UPDATE connection_credentials SET status='refresh_uncertain',revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision AND status='ready'", token, P("tenant", context.TenantId, "id", id, "revision", revision)).ConfigureAwait(false) != 1) throw new DomainException(409, "credential_changed", "The login changed before refresh. Reload its status.");
            }, token).ConfigureAwait(false);
            return revision + 1;
        }
        /// <summary>Return a claimed login to ready when its refresh request provably did not reach, or was not processed by, the login service.</summary>
        public Task ReleaseRefreshAsync(RequestContext context, string id, long revision, CancellationToken token)
        {
            return _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE connection_credentials SET status='ready',revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision AND status='refresh_uncertain'", token, P("tenant", context.TenantId, "id", id, "revision", revision)).ConfigureAwait(false);
            }, token);
        }
        /// <summary>Reencrypt every service-owned credential in one transaction, reading bounded batches.</summary>
        public async Task<long> ReencryptAsync(RequestContext context, Func<string, string, string> transform, CancellationToken token = default)
        {
            RequireWrite(context); long count = 0;
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                string after = "";
                while (true)
                {
                    Dictionary<string, string> batch = new Dictionary<string, string>(StringComparer.Ordinal);
                    using (DbCommand command = Command(connection, transaction, "SELECT id,ciphertext FROM connection_credentials WHERE id>@after ORDER BY id LIMIT 100", P("after", after)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) { after = S(reader, "id"); batch.Add(after, S(reader, "ciphertext")); } }
                    foreach (KeyValuePair<string, string> row in batch)
                    {
                        await ExecuteAsync(connection, transaction, "UPDATE connection_credentials SET ciphertext=@ciphertext WHERE id=@id", token, P("id", row.Key, "ciphertext", transform(row.Key, row.Value))).ConfigureAwait(false);
                        count++;
                    }
                    if (batch.Count < 100) break;
                }
            }, token).ConfigureAwait(false);
            return count;
        }
        /// <summary>Disable a credential without removing historical references.</summary>
        public async Task DisableAsync(RequestContext context, string id, long revision, CancellationToken token = default)
        {
            RequireWrite(context);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await ExecuteAsync(connection, transaction, "UPDATE connection_credentials SET status='disabled',revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision", token, P("tenant", context.TenantId, "id", id, "revision", revision)).ConfigureAwait(false) != 1) throw new DomainException(409, "credential_changed", "Refresh credential metadata before disabling it.");
            }, token).ConfigureAwait(false);
        }
    }
}
