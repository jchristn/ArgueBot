namespace ArgueBot.Core.Database.Implementations
{
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Global recovery resources. Only system administrators may cross tenant boundaries to operate complete-service backups.</summary>
    public sealed class BackupMethods : IBackupMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal BackupMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Create a pending archive record and audit its administrator initiator.</summary>
        public async Task<BackupEntry> CreateAsync(RequestContext context, string name, bool encrypted, bool includesSecrets, CancellationToken token = default)
        {
            RequireAdmin(context);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Contains('\0')) throw new DomainException(400, "invalid_name", "Use a backup name between 1 and 200 characters, without NUL bytes.");
            if (includesSecrets && !encrypted) throw new DomainException(400, "encryption_required", "Secret-inclusive backups must be encrypted.");
            string id = Ids.New("bak_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO backup_records(id,created_by_tenant_id,created_by_user_id,name,encrypted,includes_secrets,schema_version,provider,status,created_utc,updated_utc) VALUES(@id,@tenant,@user,@name,@encrypted,@secrets,@schema,@provider,'creating',@now,@now)", token, P("id", id, "tenant", context.TenantId, "user", context.UserId, "name", name.Trim(), "encrypted", encrypted ? 1 : 0, "secrets", includesSecrets ? 1 : 0, "schema", DatabaseSchema.CurrentVersion, "provider", _Database.ProviderType.ToString())).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "backup.create", "accepted", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }
        /// <summary>Filter and page global recovery metadata. Archive contents are not read by this operation.</summary>
        public async Task<BackupPage> EnumerateAsync(RequestContext context, string search = "", int page = 1, int size = 25, CancellationToken token = default)
        {
            RequireAdmin(context);
            if (page < 1 || size < 1 || size > 100) throw new DomainException(400, "invalid_page", "Use a positive page and page size from 1 to 100.");
            BackupPage result = new BackupPage { PageNumber = page, PageSize = size };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                result.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM backup_records WHERE instr(lower(name),lower(@search))>0", token, P("search", search)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM backup_records WHERE instr(lower(name),lower(@search))>0 ORDER BY created_utc DESC,id DESC LIMIT @limit OFFSET @offset", P("search", search, "limit", size, "offset", checked((long)(page - 1) * size))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Items.Add(ReadEntry(reader)); }
            }
            return result;
        }
        /// <summary>Read global metadata using a system-administrator authorization decision.</summary>
        public async Task<BackupEntry> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAdmin(context);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM backup_records WHERE id=@id", P("id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Recovery artifact not found."); return ReadEntry(reader); }
        }
        /// <summary>Publish archive size/checksum/status after its atomic filesystem publication, or mark a failed artifact.</summary>
        public async Task SetStatusAsync(RequestContext context, string id, string status, long bytes = 0, string digest = "", CancellationToken token = default)
        {
            RequireAdmin(context);
            if (status != "ready" && status != "failed" && status != "unavailable") throw new ArgumentException("Invalid recovery artifact status.", nameof(status));
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE backup_records SET status=@status,file_bytes=@bytes,digest=@digest,updated_utc=@now WHERE id=@id", token, P("id", id, "status", status, "bytes", bytes, "digest", digest)).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "backup.publish", status, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        /// <summary>Accept a durable create, verify or restore operation without persisting its passphrase.</summary>
        public async Task<BackupOperation> CreateOperationAsync(RequestContext context, string backupId, string kind, string targetHome = "", int port = 0, CancellationToken token = default)
        {
            await ReadAsync(context, backupId, token).ConfigureAwait(false);
            if (kind != "create" && kind != "verify" && kind != "restore") throw new ArgumentException("Invalid recovery operation kind.", nameof(kind));
            string id = Ids.New("bop_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO backup_operations(id,backup_id,created_by_tenant_id,created_by_user_id,kind,state,target_home,port,created_utc,updated_utc) VALUES(@id,@backup,@tenant,@user,@kind,'queued',@home,@port,@now,@now)", token, P("id", id, "backup", backupId, "tenant", context.TenantId, "user", context.UserId, "kind", kind, "home", targetHome, "port", port)).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "backup." + kind, "queued", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadOperationAsync(context, id, token).ConfigureAwait(false);
        }
        /// <summary>Read a durable operation and the names of missing credential references.</summary>
        public async Task<BackupOperation> ReadOperationAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAdmin(context); BackupOperation operation;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT * FROM backup_operations WHERE id=@id", P("id", id)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Recovery operation not found.");
                    operation = new BackupOperation { Id = id, BackupId = S(reader, "backup_id"), Kind = S(reader, "kind"), State = S(reader, "state"), ErrorCode = S(reader, "error_code"), TargetHome = S(reader, "target_home"), Port = Convert.ToInt32(reader["port"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
                }
                using (DbCommand command = Command(connection, null, "SELECT name FROM backup_operation_references WHERE operation_id=@id ORDER BY name", P("id", id)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) operation.MissingReferences.Add(reader.GetString(0)); }
            }
            return operation;
        }
        /// <summary>Checkpoint a service-owned recovery task and its nonsecret credential diagnostics.</summary>
        public Task SaveOperationAsync(RequestContext context, BackupOperation operation, CancellationToken token = default)
        {
            RequireAdmin(context);
            ArgumentNullException.ThrowIfNull(operation);
            if (operation.State != "queued" && operation.State != "running" && operation.State != "completed" && operation.State != "failed") throw new ArgumentException("Invalid operation state.", nameof(operation));
            if (!System.Text.RegularExpressions.Regex.IsMatch(operation.ErrorCode, "^[a-z_]{0,80}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid safe error code.", nameof(operation));
            foreach (string reference in operation.MissingReferences)
                if (!System.Text.RegularExpressions.Regex.IsMatch(reference, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid credential reference.", nameof(operation));
            return _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE backup_operations SET state=@state,error_code=@error,updated_utc=@now WHERE id=@id", token, P("id", operation.Id, "state", operation.State, "error", operation.ErrorCode)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM backup_operation_references WHERE operation_id=@id", token, P("id", operation.Id)).ConfigureAwait(false);
                foreach (string reference in operation.MissingReferences)
                    await ExecuteAsync(connection, transaction, "INSERT INTO backup_operation_references(id,operation_id,name,created_utc,updated_utc) VALUES(@id,@operation,@name,@now,@now)", token, P("id", Ids.New("bref_"), "operation", operation.Id, "name", reference)).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "backup." + operation.Kind, operation.State, token).ConfigureAwait(false);
            }, token);
        }
        /// <summary>Refuse file mutation while an accepted recovery operation still owns the artifact. The service serializes acceptance and deletion around this check.</summary>
        public async Task EnsureIdleAsync(RequestContext context, string id, CancellationToken token = default)
        {
            await ReadAsync(context, id, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                long count = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM backup_operations WHERE backup_id=@id AND state IN ('queued','running')", token, P("id", id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (count > 0) throw new DomainException(409, "backup_busy", "Wait for this artifact's active operation before deleting it.");
            }
        }
        /// <summary>Delete an artifact's metadata after its file has been staged for removal. In-flight operations prevent deletion.</summary>
        public async Task DeleteAsync(RequestContext context, string id, CancellationToken token = default)
        {
            await ReadAsync(context, id, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                long active = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM backup_operations WHERE backup_id=@id AND state IN ('queued','running')", token, P("id", id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (active > 0) throw new DomainException(409, "backup_busy", "Wait for this artifact's active operation before deleting it.");
                await ExecuteAsync(connection, transaction, "DELETE FROM backup_records WHERE id=@id", token, P("id", id)).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "backup.delete", "completed", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        /// <summary>Service startup reconciliation only: never automatically replay interrupted administrative operations.</summary>
        public Task ReconcileAsync(CancellationToken token = default)
        {
            return _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE backup_operations SET state='failed',error_code='service_interrupted',updated_utc=@now WHERE state IN ('queued','running'); UPDATE backup_records SET status='unavailable',updated_utc=@now WHERE status='creating'", token).ConfigureAwait(false);
            }, token);
        }
        private static void RequireAdmin(RequestContext context)
        {
            if (!context.IsAuthenticated) throw new DomainException(401, "unauthenticated", "Authentication required.");
            if (!context.IsAdmin) throw new DomainException(403, "forbidden", "Complete-service recovery requires a system administrator.");
        }
        private static BackupEntry ReadEntry(DbDataReader reader)
        {
            return new BackupEntry { Id = S(reader, "id"), Name = S(reader, "name"), Bytes = Convert.ToInt64(reader["file_bytes"], CultureInfo.InvariantCulture), Encrypted = Convert.ToInt32(reader["encrypted"], CultureInfo.InvariantCulture) != 0, IncludesSecrets = Convert.ToInt32(reader["includes_secrets"], CultureInfo.InvariantCulture) != 0, SchemaVersion = Convert.ToInt32(reader["schema_version"], CultureInfo.InvariantCulture), Provider = S(reader, "provider"), Digest = S(reader, "digest"), Status = S(reader, "status"), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
        }
        private static Task<int> AuditAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string operation, string outcome, CancellationToken token)
        { return ExecuteAsync(connection, transaction, "INSERT INTO audit_events(id,tenant_id,user_id,operation,outcome,created_utc,updated_utc) VALUES(@id,@tenant,@user,@operation,@outcome,@now,@now)", token, P("id", Ids.New("aud_"), "tenant", context.TenantId, "user", context.UserId, "operation", operation, "outcome", outcome)); }
    }
}
