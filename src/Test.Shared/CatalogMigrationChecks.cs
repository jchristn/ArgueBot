namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using Microsoft.Data.Sqlite;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class CatalogMigrationChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-catalog-upgrade-" + Guid.NewGuid().ToString("N"));
            ArgueBotPaths paths = new ArgueBotPaths(home);
            RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
            string cliId; string restId; string conversationId; string runId;
            CreateRunRequest request;
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    cliId = (await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Legacy CLI", Executable = "fixture", Arguments = new List<string> { "", "--literal", "Unicode 議論" } }, token).ConfigureAwait(false)).Id;
                    restId = (await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Legacy REST", Kind = "rest", Provider = "openai", Endpoint = "http://127.0.0.1:4491", Model = "fixture-model", SecretEnvironmentVariable = "ARGUEBOT_FICTIONAL_MIGRATION_KEY", TimeoutSeconds = 47 }, token).ConfigureAwait(false)).Id;
                    conversationId = (await database.CreateConversationAsync(context, "Legacy durable run", token).ConfigureAwait(false)).Id;
                    request = new CreateRunRequest { ParticipantIds = new List<string> { cliId, restId }, IdempotencyKey = "old-client-command" };
                    runId = (await database.Runs.CreateAsync(context, conversationId, request, token).ConfigureAwait(false)).Id;
                }
                // Reproduce the pre-catalog schema while retaining genuine profiles, arguments, snapshots and command receipts.
                using (SqliteConnection connection = new SqliteConnection("Data Source=" + paths.DatabaseFile))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA foreign_keys=OFF; ALTER TABLE debate_state DROP COLUMN compaction_recipient_id; DROP TABLE connection_arguments; DROP TABLE connection_bindings; DROP TABLE connection_profiles; DROP TABLE connection_models; DROP TABLE connection_harnesses; DROP TABLE connection_endpoints; DROP TABLE connection_credentials; DROP TABLE debate_tool_reservations; DROP TABLE debate_attempt_usage; DROP TABLE debate_policies; DELETE FROM schema_migrations WHERE version>=14;";
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                for (int pass = 0; pass < 2; pass++)
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RunnerProfile cli = await database.Profiles.ReadAsync(context, cliId, token).ConfigureAwait(false);
                    RunnerProfile rest = await database.Profiles.ReadAsync(context, restId, token).ConfigureAwait(false);
                    if (cli.ModelProfileId.Length == 0 || rest.ModelProfileId.Length == 0) throw new InvalidDataException("Legacy reusable profiles were not linked to the catalog.");
                    await database.Catalog.ResolveAsync(context, cli, token).ConfigureAwait(false);
                    await database.Catalog.ResolveAsync(context, rest, token).ConfigureAwait(false);
                    if (!cli.Arguments.SequenceEqual(new[] { "", "--literal", "Unicode 議論" }) || rest.TimeoutSeconds != 47 || rest.Model != "fixture-model" || rest.AuthBindings.Single().EnvironmentVariable != "ARGUEBOT_FICTIONAL_MIGRATION_KEY") throw new InvalidDataException("Migration changed accepted transport settings or literal arguments.");
                    DebateRun historical = await database.Runs.CreateAsync(context, conversationId, request, token).ConfigureAwait(false);
                    if (historical.Id != runId || historical.Participants.Any(profile => profile.ModelProfileId.Length != 0)) throw new InvalidDataException("Migration rewrote a historical snapshot or invalidated an old command receipt.");
                    if ((await database.Catalog.ModelsAsync(context, token).ConfigureAwait(false)).Count != 2) throw new InvalidDataException("Repeated initialization duplicated migrated resources.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
