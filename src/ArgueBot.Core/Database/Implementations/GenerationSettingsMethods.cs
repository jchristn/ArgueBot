namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Models;
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    internal static class GenerationSettingsMethods
    {
        internal static async Task SaveAsync(DbConnection connection, DbTransaction transaction, string tenant, string id, ModelGenerationSettings settings, int? outputBytes, CancellationToken token)
        {
            string table = outputBytes.HasValue ? "connection_model_generation" : "connection_profile_generation";
            await ExecuteAsync(connection, transaction, "DELETE FROM " + table + " WHERE tenant_id=@tenant AND id=@id", token, P("tenant", tenant, "id", id)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO " + table + "(id,tenant_id,output_tokens,temperature,top_p," + (outputBytes.HasValue ? "output_bytes," : "") + "created_utc,updated_utc) VALUES(@id,@tenant,@tokens,@temperature,@top," + (outputBytes.HasValue ? "@bytes," : "") + "@now,@now)", token, P("tenant", tenant, "id", id, "tokens", settings.MaxOutputTokens, "temperature", settings.Temperature?.ToString("R", CultureInfo.InvariantCulture), "top", settings.TopP?.ToString("R", CultureInfo.InvariantCulture), "bytes", outputBytes)).ConfigureAwait(false);
        }
        internal static async Task LoadModelAsync(DbConnection connection, string tenant, ModelProfile model, CancellationToken token)
        {
            using (DbCommand command = Command(connection, null, "SELECT * FROM connection_model_generation WHERE tenant_id=@tenant AND id=@id", P("tenant", tenant, "id", model.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return;
                model.MaxOutputBytes = Convert.ToInt32(reader["output_bytes"], CultureInfo.InvariantCulture);
                model.Generation = Read(reader);
            }
        }
        internal static async Task LoadProfileAsync(DbConnection connection, DbTransaction? transaction, string tenant, RunnerProfile profile, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM connection_profile_generation WHERE tenant_id=@tenant AND id=@id", P("tenant", tenant, "id", profile.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (await reader.ReadAsync(token).ConfigureAwait(false)) profile.Generation = Read(reader); }
        }
        private static ModelGenerationSettings Read(DbDataReader reader)
        {
            return new ModelGenerationSettings
            {
                MaxOutputTokens = reader["output_tokens"] is DBNull ? null : Convert.ToInt32(reader["output_tokens"], CultureInfo.InvariantCulture),
                Temperature = reader["temperature"] is DBNull ? null : double.Parse(S(reader, "temperature"), CultureInfo.InvariantCulture),
                TopP = reader["top_p"] is DBNull ? null : double.Parse(S(reader, "top_p"), CultureInfo.InvariantCulture)
            };
        }
    }
}
