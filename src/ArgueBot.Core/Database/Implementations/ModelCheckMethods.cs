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

    /// <summary>Configuration checks keep accepted snapshots and safe observations, never response bodies.</summary>
    public sealed class ModelCheckMethods : IModelCheckMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal ModelCheckMethods(DatabaseDriverBase database) { _Database = database; }
        /// <inheritdoc />
        public async Task<ModelCheck> CreateAsync(RequestContext context, string modelId, ModelCheckRequest request, CancellationToken token = default)
        {
            RequireWrite(context);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200) throw new DomainException(400, "invalid_command", "Provide a stable check command key up to 200 characters.");
            ModelCheck? check = null;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM connection_model_checks WHERE tenant_id=@tenant AND model_id=@model AND command_key=@key", P("tenant", context.TenantId, "model", modelId, "key", request.IdempotencyKey)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (await reader.ReadAsync(token).ConfigureAwait(false)) check = Read(reader); }
            if (check != null)
            {
                if (check.CheckTools != request.CheckTools) throw new DomainException(409, "idempotency_conflict", "The check key already has different input.");
                await LoadSnapshotAsync(context, check, token).ConfigureAwait(false); return check;
            }
            RunnerProfile profile = new RunnerProfile { Id = Ids.New("par_"), Name = "Explicit model check", ModelProfileId = modelId };
            await _Database.Catalog.ResolveAsync(context, profile, token).ConfigureAwait(false);
            profile.TimeoutSeconds = Math.Min(30, profile.TimeoutSeconds); profile.MaxOutputBytes = 8192;
            if (request.CheckTools && !profile.SupportsTools) throw new DomainException(400, "tools_not_declared", "Enable declared tool support before explicitly checking the tool round trip.");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                using (DbCommand command = Command(connection, transaction, "SELECT * FROM connection_model_checks WHERE tenant_id=@tenant AND model_id=@model AND command_key=@key", P("tenant", context.TenantId, "model", modelId, "key", request.IdempotencyKey)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        check = Read(reader);
                        if (check.CheckTools != request.CheckTools) throw new DomainException(409, "idempotency_conflict", "The check key already has different input.");
                        return;
                    }
                }
                await ProfileMethods.InsertAsync(connection, transaction, context, profile, true, token).ConfigureAwait(false);
                string id = Ids.New("chk_");
                await ExecuteAsync(connection, transaction, "INSERT INTO connection_model_checks(id,tenant_id,model_id,profile_id,command_key,check_tools,state,created_utc,updated_utc) VALUES(@id,@tenant,@model,@profile,@key,@tools,'queued',@now,@now)", token, P("id", id, "tenant", context.TenantId, "model", modelId, "profile", profile.Id, "key", request.IdempotencyKey, "tools", request.CheckTools ? 1 : 0)).ConfigureAwait(false);
                check = new ModelCheck { Id = id, ModelId = modelId, CheckTools = request.CheckTools, Snapshot = profile, ModelRevision = profile.ModelRevision, TransportId = profile.TransportId, TransportRevision = profile.TransportRevision, NewlyAccepted = true, CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
            }, token).ConfigureAwait(false);
            if (!check!.NewlyAccepted) await LoadSnapshotAsync(context, check, token).ConfigureAwait(false);
            return check;
        }
        /// <inheritdoc />
        public async Task<List<ModelCheck>> EnumerateAsync(RequestContext context, string modelId, int pageNumber = 1, CancellationToken token = default)
        {
            RequireAuthenticated(context); CatalogMethods.ValidatePage(pageNumber, 25);
            List<ModelCheck> result = new List<ModelCheck>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM connection_model_checks WHERE tenant_id=@tenant AND model_id=@model ORDER BY created_utc DESC,id DESC LIMIT 25 OFFSET @offset", P("tenant", context.TenantId, "model", modelId, "offset", ((long)pageNumber - 1) * 25)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Read(reader)); }
            foreach (ModelCheck check in result) await LoadSnapshotAsync(context, check, token).ConfigureAwait(false);
            return result;
        }
        private async Task LoadSnapshotAsync(RequestContext context, ModelCheck check, CancellationToken token)
        {
            check.Snapshot = await _Database.Profiles.ReadAsync(context, check.Snapshot.Id, token).ConfigureAwait(false);
            check.ModelRevision = check.Snapshot.ModelRevision; check.TransportId = check.Snapshot.TransportId; check.TransportRevision = check.Snapshot.TransportRevision;
        }
        private static ModelCheck Read(DbDataReader reader)
        { return new ModelCheck { Id = S(reader, "id"), ModelId = S(reader, "model_id"), Snapshot = new RunnerProfile { Id = S(reader, "profile_id") }, CheckTools = Convert.ToInt32(reader["check_tools"], CultureInfo.InvariantCulture) != 0, State = S(reader, "state"), CompletionVerified = Convert.ToInt32(reader["completion_verified"], CultureInfo.InvariantCulture) != 0, ToolsVerified = Convert.ToInt32(reader["tools_verified"], CultureInfo.InvariantCulture) != 0, ErrorCode = S(reader, "error_code"), ExecutableVersion = S(reader, "executable_version"), DurationMilliseconds = Convert.ToInt64(reader["duration_ms"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) }; }
        /// <inheritdoc />
        public Task UpdateAsync(RequestContext context, ModelCheck check, CancellationToken token = default)
        {
            RequireWrite(context); check.UpdatedUtc = DateTime.UtcNow;
            return _Database.WriteAsync(async (connection, transaction) =>
            { await ExecuteAsync(connection, transaction, "UPDATE connection_model_checks SET state=@state,completion_verified=@completion,tools_verified=@tools,error_code=@error,executable_version=@version,duration_ms=@duration,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND state IN ('queued','running')", token, P("tenant", context.TenantId, "id", check.Id, "state", check.State, "completion", check.CompletionVerified ? 1 : 0, "tools", check.ToolsVerified ? 1 : 0, "error", check.ErrorCode, "version", check.ExecutableVersion, "duration", check.DurationMilliseconds)).ConfigureAwait(false); }, token);
        }
        /// <inheritdoc />
        public Task ReconcileAsync(CancellationToken token = default)
        { return _Database.WriteAsync(async (connection, transaction) => { await ExecuteAsync(connection, transaction, "UPDATE connection_model_checks SET state='uncertain',error_code='service_interrupted',updated_utc=@now WHERE state IN ('queued','running')", token, P()).ConfigureAwait(false); }, token); }
    }
}
