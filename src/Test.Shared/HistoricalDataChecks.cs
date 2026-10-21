namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using Microsoft.Data.Sqlite;

    internal static class HistoricalDataChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-historical-" + Guid.NewGuid().ToString("N"));
            ArgueBotPaths paths = new ArgueBotPaths(home);
            RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true };
            string conversationId;
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(context, "Retained historical conversation", token).ConfigureAwait(false);
                    conversationId = conversation.Id;
                    await database.AppendMessageAsync(context, new Message { ConversationId = conversationId, Content = "Retain this history." }, token: token).ConfigureAwait(false);
                }
                using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString()))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "UPDATE users SET active=0,password_sha256=@hash; UPDATE tenants SET active=0; INSERT INTO auth_sessions(id,tenant_id,user_id,token_hash,expires_utc,created_utc,updated_utc) VALUES('ses_historical','ten_local','usr_local','fixture-historical-digest','2027-01-01T00:00:00Z','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z'); ALTER TABLE users DROP COLUMN password_salt; ALTER TABLE users DROP COLUMN password_iterations; DROP TABLE authorization_user_assignments; DROP TABLE authorization_role_permissions; DROP TABLE authorization_permissions; DROP TABLE authorization_roles; DELETE FROM schema_migrations WHERE version>=11;";
                        command.Parameters.AddWithValue("@hash", new string('A', 64));
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                using (SqliteDatabaseDriver upgraded = new SqliteDatabaseDriver(paths))
                {
                    await upgraded.InitializeAsync(token).ConfigureAwait(false);
                    if ((await upgraded.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false)).Title != "Retained historical conversation") throw new InvalidDataException("Historical upgrade changed the conversation.");
                    if ((await upgraded.ReadMessagesAsync(context, conversationId, token).ConfigureAwait(false)).Count != 1) throw new InvalidDataException("Historical upgrade lost transcript history.");
                    RunnerProfile profile = await upgraded.Profiles.CreateAsync(context, new RunnerProfile { Name = "Historical fixture", Executable = "fixture" }, token).ConfigureAwait(false);
                    await using (ConversationEngine engine = new ConversationEngine(upgraded, new FixtureRunner(), 1))
                    {
                        DebateRun run = await engine.StartAsync(context, conversationId, new CreateRunRequest { Mode = "followup", Question = "Continue in the open workspace.", ParticipantIds = new List<string> { profile.Id }, IdempotencyKey = "historical-followup" }, token).ConfigureAwait(false);
                        using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            deadline.CancelAfter(TimeSpan.FromSeconds(5));
                            while ((await upgraded.Runs.ReadAsync(context, conversationId, run.Id, deadline.Token).ConfigureAwait(false)).State != "completed") await Task.Delay(10, deadline.Token).ConfigureAwait(false);
                        }
                    }
                }
                using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString()))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT active,password_sha256,password_salt,password_iterations FROM users WHERE id='usr_local'";
                        using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(0) != 0 || reader.GetString(1) == new string('A', 64) || reader.GetString(2).Length != 32 || reader.GetInt32(3) < 600000) throw new InvalidDataException("Historical credential conversion or inactive metadata was not preserved.");
                        }
                        command.CommandText = "SELECT COUNT(*) FROM auth_sessions WHERE id='ses_historical'";
                        if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) != 1) throw new InvalidDataException("Startup deleted historical session data.");
                        command.CommandText = "SELECT COUNT(*) FROM authorization_roles";
                        if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) != 0) throw new InvalidDataException("Startup reseeded removed account administration.");
                    }
                }
                if (Directory.GetFiles(Path.Combine(home, "backups"), "before-schema-10-*.db").Length != 1) throw new InvalidDataException("Historical upgrade omitted its safety snapshot.");
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
