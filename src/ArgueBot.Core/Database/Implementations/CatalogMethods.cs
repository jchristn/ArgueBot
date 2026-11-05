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
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Domain operations for reusable endpoints and models.</summary>
    public sealed class CatalogMethods : ICatalogMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal CatalogMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>List reusable endpoint configuration, including disabled records.</summary>
        public async Task<List<EndpointProfile>> EndpointsAsync(RequestContext context, CancellationToken token = default, string id = "", int pageNumber = 1, int pageSize = 100)
        {
            RequireAuthenticated(context); ValidatePage(pageNumber, pageSize); List<EndpointProfile> result = new List<EndpointProfile>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT * FROM connection_endpoints WHERE tenant_id=@tenant AND (@id='' OR id=@id) ORDER BY name,id LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "id", id, "limit", pageSize, "offset", ((long)pageNumber - 1) * pageSize)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new EndpointProfile { Id = S(reader, "id"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), Name = S(reader, "name"), Protocol = S(reader, "protocol"), BaseUrl = S(reader, "base_url"), TimeoutSeconds = Convert.ToInt32(reader["timeout_seconds"], CultureInfo.InvariantCulture), MaxConcurrentInvocations = Convert.ToInt32(reader["max_concurrent_invocations"], CultureInfo.InvariantCulture), Enabled = Convert.ToInt32(reader["enabled"], CultureInfo.InvariantCulture) != 0 }); }
                foreach (EndpointProfile item in result) await LoadBindingsAsync(connection, null, context.TenantId, item.Id, item.AuthBindings, token).ConfigureAwait(false);
            }
            return result;
        }
        /// <summary>Create or revision-check an update to a reusable endpoint.</summary>
        public async Task<EndpointProfile> SaveEndpointAsync(RequestContext context, EndpointProfile item, CancellationToken token = default)
        {
            RequireWrite(context);
            if (item.AuthBindings.Any(binding => binding != null && binding.CredentialId.Length > 0))
            {
                Dictionary<string, CredentialMetadata> credentials = new Dictionary<string, CredentialMetadata>(StringComparer.Ordinal);
                foreach (string id in item.AuthBindings.Where(binding => binding != null && binding.CredentialId.Length > 0).Select(binding => binding.CredentialId).Distinct(StringComparer.Ordinal))
                    foreach (CredentialMetadata credential in await _Database.Credentials.EnumerateAsync(context, token, id).ConfigureAwait(false)) credentials[credential.Id] = credential;
                CatalogValidation.Place(item.AuthBindings, credentials);
            }
            CatalogValidation.Validate(item);
            bool create = item.Id.Length == 0;
            if (create) item.Id = Ids.New("end_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Dictionary<string, object?> values = P("id", item.Id, "tenant", context.TenantId, "revision", item.Revision, "name", item.Name, "protocol", item.Protocol, "base_url", item.BaseUrl, "timeout_seconds", item.TimeoutSeconds, "capacity", item.MaxConcurrentInvocations, "enabled", item.Enabled ? 1 : 0);
                if (create) await ExecuteAsync(connection, transaction, "INSERT INTO connection_endpoints(id,tenant_id,name,protocol,base_url,timeout_seconds,max_concurrent_invocations,enabled,created_utc,updated_utc) VALUES(@id,@tenant,@name,@protocol,@base_url,@timeout_seconds,@capacity,@enabled,@now,@now)", token, values).ConfigureAwait(false);
                else if (await ExecuteAsync(connection, transaction, "UPDATE connection_endpoints SET name=@name,protocol=@protocol,base_url=@base_url,timeout_seconds=@timeout_seconds,max_concurrent_invocations=@capacity,enabled=@enabled,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision", token, values).ConfigureAwait(false) != 1) throw new DomainException(409, "revision_conflict", "Configuration changed. Refresh before saving.");
                await SaveBindingsAsync(connection, transaction, context.TenantId, item.Id, item.AuthBindings, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            item.Revision = create ? 1 : item.Revision + 1; return item;
        }
        /// <summary>List reusable model configuration, including disabled records.</summary>
        public async Task<List<ModelProfile>> ModelsAsync(RequestContext context, CancellationToken token = default, string id = "", int pageNumber = 1, int pageSize = 100)
        {
            RequireAuthenticated(context); ValidatePage(pageNumber, pageSize); List<ModelProfile> result = new List<ModelProfile>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT * FROM connection_models WHERE tenant_id=@tenant AND (@id='' OR id=@id) ORDER BY name,id LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "id", id, "limit", pageSize, "offset", ((long)pageNumber - 1) * pageSize)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new ModelProfile { Id = S(reader, "id"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), Name = S(reader, "name"), EndpointId = S(reader, "endpoint_id"), Model = S(reader, "model"), ContextMaxCharacters = Convert.ToInt32(reader["context_characters"], CultureInfo.InvariantCulture), SupportsTools = Convert.ToInt32(reader["supports_tools"], CultureInfo.InvariantCulture) != 0, Enabled = Convert.ToInt32(reader["enabled"], CultureInfo.InvariantCulture) != 0 }); }
                foreach (ModelProfile model in result) await GenerationSettingsMethods.LoadModelAsync(connection, context.TenantId, model, token).ConfigureAwait(false);
            }
            return result;
        }
        /// <summary>Create or revision-check an update to a reusable model.</summary>
        public async Task<ModelProfile> SaveModelAsync(RequestContext context, ModelProfile item, CancellationToken token = default)
        {
            RequireWrite(context); CatalogValidation.Validate(item);
            if (item.EndpointId.Length > 0 && !(await EndpointsAsync(context, token, item.EndpointId).ConfigureAwait(false)).Any()) throw Missing();
            bool create = item.Id.Length == 0;
            if (create) item.Id = Ids.New("mod_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Dictionary<string, object?> values = P("id", item.Id, "tenant", context.TenantId, "revision", item.Revision, "name", item.Name, "endpoint_id", item.EndpointId, "harness_id", "", "model", item.Model, "context_characters", item.ContextMaxCharacters, "supports_tools", item.SupportsTools ? 1 : 0, "enabled", item.Enabled ? 1 : 0);
                if (create) await ExecuteAsync(connection, transaction, "INSERT INTO connection_models(id,tenant_id,name,endpoint_id,harness_id,model,context_characters,supports_tools,enabled,created_utc,updated_utc) VALUES(@id,@tenant,@name,@endpoint_id,@harness_id,@model,@context_characters,@supports_tools,@enabled,@now,@now)", token, values).ConfigureAwait(false);
                else if (await ExecuteAsync(connection, transaction, "UPDATE connection_models SET name=@name,endpoint_id=@endpoint_id,harness_id=@harness_id,model=@model,context_characters=@context_characters,supports_tools=@supports_tools,enabled=@enabled,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision", token, values).ConfigureAwait(false) != 1) throw new DomainException(409, "revision_conflict", "Configuration changed. Refresh before saving.");
                await GenerationSettingsMethods.SaveAsync(connection, transaction, context.TenantId, item.Id, item.Generation, item.MaxOutputBytes, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            item.Revision = create ? 1 : item.Revision + 1; return item;
        }
        internal static void ValidatePage(int pageNumber, int pageSize)
        { if (pageNumber < 1 || pageSize < 1 || pageSize > 100) throw new DomainException(400, "invalid_page", "Use a positive page number and a page size from 1 through 100."); }
        private static DomainException Missing() { return new DomainException(409, "configuration_unavailable", "The referenced model or endpoint is unavailable or disabled. Choose an enabled configuration."); }
        /// <summary>Resolve a reusable model at admission; subsequent snapshots retain these exact nonsecret settings.</summary>
        public async Task ResolveAsync(RequestContext context, RunnerProfile profile, CancellationToken token = default)
        {
            if (profile.ModelProfileId.Length == 0) return;
            ModelProfile model = (await ModelsAsync(context, token, profile.ModelProfileId).ConfigureAwait(false)).FirstOrDefault(item => item.Enabled) ?? throw Missing();
            profile.Model = model.Model; profile.ModelRevision = model.Revision; profile.ContextMaxCharacters = model.ContextMaxCharacters; profile.SupportsTools = model.SupportsTools; profile.MaxOutputBytes = model.MaxOutputBytes; profile.Generation = model.Generation;
            profile.EnvironmentVariables.Clear(); profile.RestrictEnvironment = false;
            profile.AuthBindings.Clear(); profile.CredentialId = ""; profile.SecretEnvironmentVariable = "";
            EndpointProfile endpoint = (await EndpointsAsync(context, token, model.EndpointId).ConfigureAwait(false)).FirstOrDefault(item => item.Enabled) ?? throw Missing();
            profile.Kind = "rest"; profile.Provider = endpoint.Protocol; profile.Endpoint = endpoint.BaseUrl; profile.TimeoutSeconds = endpoint.TimeoutSeconds;
            profile.AuthBindings = endpoint.AuthBindings; profile.TransportRevision = endpoint.Revision; profile.TransportId = endpoint.Id; profile.Executable = ""; profile.Arguments.Clear();
        }
        internal static async Task SaveProfileAsync(DbConnection connection, DbTransaction transaction, RequestContext context, RunnerProfile profile, CancellationToken token)
        {
            await ExecuteAsync(connection, transaction, "DELETE FROM connection_profiles WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", profile.Id)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO connection_profiles(id,tenant_id,model_profile_id,credential_id,role_instructions,context_characters,supports_tools,model_revision,transport_revision,transport_id,created_utc,updated_utc) VALUES(@id,@tenant,@model,@credential,@role,@context,@tools,@modelrevision,@transportrevision,@transportid,@now,@now)", token, P("id", profile.Id, "tenant", context.TenantId, "model", profile.ModelProfileId, "credential", profile.CredentialId, "role", profile.RoleInstructions, "context", profile.ContextMaxCharacters, "tools", profile.SupportsTools ? 1 : 0, "modelrevision", profile.ModelRevision, "transportrevision", profile.TransportRevision, "transportid", profile.TransportId)).ConfigureAwait(false);
            await SaveBindingsAsync(connection, transaction, context.TenantId, profile.Id, profile.AuthBindings, token).ConfigureAwait(false);
            await GenerationSettingsMethods.SaveAsync(connection, transaction, context.TenantId, profile.Id, profile.Generation, null, token).ConfigureAwait(false);
            await HarnessEnvironmentMethods.SaveAsync(connection, transaction, context.TenantId, profile.Id, profile.RestrictEnvironment, profile.EnvironmentVariables, token).ConfigureAwait(false);
        }
        internal static async Task LoadProfileAsync(DbConnection connection, DbTransaction? transaction, RunnerProfile profile, CancellationToken token)
        {
            string tenant = "";
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM connection_profiles WHERE id=@id", P("id", profile.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    tenant = S(reader, "tenant_id"); profile.ModelProfileId = S(reader, "model_profile_id"); profile.CredentialId = S(reader, "credential_id"); profile.RoleInstructions = S(reader, "role_instructions");
                    profile.ContextMaxCharacters = Convert.ToInt32(reader["context_characters"], CultureInfo.InvariantCulture); profile.SupportsTools = Convert.ToInt32(reader["supports_tools"], CultureInfo.InvariantCulture) != 0;
                    profile.ModelRevision = Convert.ToInt64(reader["model_revision"], CultureInfo.InvariantCulture); profile.TransportRevision = Convert.ToInt64(reader["transport_revision"], CultureInfo.InvariantCulture); profile.TransportId = S(reader, "transport_id");
                }
            }
            if (tenant.Length > 0)
            {
                await LoadBindingsAsync(connection, transaction, tenant, profile.Id, profile.AuthBindings, token).ConfigureAwait(false);
                await GenerationSettingsMethods.LoadProfileAsync(connection, transaction, tenant, profile, token).ConfigureAwait(false);
                profile.RestrictEnvironment = await HarnessEnvironmentMethods.LoadAsync(connection, transaction, tenant, profile.Id, profile.EnvironmentVariables, token).ConfigureAwait(false);
            }
        }
        internal static async Task LoadBindingsAsync(DbConnection connection, DbTransaction? transaction, string tenant, string owner, List<HttpAuthBinding> result, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM connection_bindings WHERE tenant_id=@tenant AND owner_id=@owner ORDER BY position", P("tenant", tenant, "owner", owner)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new HttpAuthBinding { Kind = S(reader, "kind"), Name = S(reader, "name"), Value = S(reader, "value"), CredentialId = S(reader, "credential_id"), EnvironmentVariable = S(reader, "environment_variable") }); }
        }
        internal static async Task SaveBindingsAsync(DbConnection connection, DbTransaction transaction, string tenant, string owner, List<HttpAuthBinding> items, CancellationToken token)
        {
            await ExecuteAsync(connection, transaction, "DELETE FROM connection_bindings WHERE tenant_id=@tenant AND owner_id=@owner", token, P("tenant", tenant, "owner", owner)).ConfigureAwait(false);
            for (int index = 0; index < items.Count; index++)
            {
                HttpAuthBinding item = items[index];
                await ExecuteAsync(connection, transaction, "INSERT INTO connection_bindings(id,tenant_id,owner_id,position,kind,name,value,credential_id,environment_variable,created_utc,updated_utc) VALUES(@id,@tenant,@owner,@position,@kind,@name,@value,@credential,@environment,@now,@now)", token, P("id", Ids.New("bnd_"), "tenant", tenant, "owner", owner, "position", index, "kind", item.Kind, "name", item.Name, "value", item.Value, "credential", item.CredentialId, "environment", item.EnvironmentVariable)).ConfigureAwait(false);
            }
        }
    }
}
