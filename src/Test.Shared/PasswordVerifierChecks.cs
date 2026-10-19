namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;

    internal static class PasswordVerifierChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-password-verifiers-" + Guid.NewGuid().ToString("N"));
            try
            {
                ArgueBotPaths paths = new ArgueBotPaths(home);
                RequestContext owner = new RequestContext { IsAuthenticated = true, TenantId = "ten_local", UserId = "usr_local", IsAdmin = true, Role = "admin" };
                string equivalent = new string('F', 64); List<string> ids = new List<string>(); string session;
                TokenProtector protector;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false); protector = new TokenProtector(paths);
                    foreach (string name in new[] { "first", "second" }) ids.Add((await database.Users.CreateAsync(owner, new UserRequest { Name = name, Email = name + "@example.invalid", PasswordSha256 = equivalent }, token).ConfigureAwait(false)).Id);
                    SessionMethods sessions = new SessionMethods(database, protector);
                    session = await sessions.LoginAsync(new LoginRequest { TenantId = "ten_local", Email = "first@example.invalid", PasswordSha256 = equivalent }, token: token).ConfigureAwait(false);
                    await CheckStorageAsync(paths, sessions, equivalent, token).ConfigureAwait(false);
                }
                // Reconstruct a real schema-10 source: it stored the login equivalent directly.
                // Both fictional accounts share the known input so no salted format is mislabelled as legacy.
                using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString()))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "UPDATE users SET password_sha256=@equivalent WHERE tenant_id='ten_local' AND email IN ('first@example.invalid','second@example.invalid'); ALTER TABLE users DROP COLUMN password_salt; ALTER TABLE users DROP COLUMN password_iterations; DELETE FROM schema_migrations WHERE version=11;";
                        command.Parameters.AddWithValue("@equivalent", equivalent); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                using (SqliteDatabaseDriver upgraded = new SqliteDatabaseDriver(paths))
                {
                    await upgraded.InitializeAsync(token).ConfigureAwait(false);
                    SessionMethods sessions = new SessionMethods(upgraded, protector);
                    if ((await sessions.AuthenticateAsync(session, token).ConfigureAwait(false))?.UserId != ids[0]) throw new InvalidDataException("Verifier migration changed existing session or identity bindings.");
                    foreach (string name in new[] { "first", "second" }) await sessions.LoginAsync(new LoginRequest { TenantId = "ten_local", Email = name + "@example.invalid", PasswordSha256 = equivalent }, token: token).ConfigureAwait(false);
                    await CheckStorageAsync(paths, sessions, equivalent, token).ConfigureAwait(false);
                }
                if (Directory.GetFiles(Path.Combine(home, "backups"), "before-schema-10-*.db").Length != 1) throw new InvalidDataException("Password verifier migration skipped its protected pre-upgrade backup.");
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task CheckStorageAsync(ArgueBotPaths paths, SessionMethods sessions, string equivalent, CancellationToken token)
        {
            List<string> hashes = new List<string>(); List<string> salts = new List<string>();
            using (SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = false }.ToString()))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT password_sha256,password_salt,password_iterations FROM users WHERE tenant_id='ten_local' AND email IN ('first@example.invalid','second@example.invalid') ORDER BY email";
                    using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            hashes.Add(reader.GetString(0)); salts.Add(reader.GetString(1));
                            if (reader.GetInt32(2) < 600000 || hashes[hashes.Count - 1] == equivalent || hashes[hashes.Count - 1].Length != 64 || salts[salts.Count - 1].Length != 32) throw new InvalidDataException("Stored password material remained replayable or lost its bounded work/salt parameters.");
                        }
                    }
                }
            }
            if (hashes.Count != 2 || hashes[0] == hashes[1] || salts[0] == salts[1]) throw new InvalidDataException("Equal passwords reused verifier material across identities.");
            bool rejected = false;
            try { await sessions.LoginAsync(new LoginRequest { TenantId = "ten_local", Email = "first@example.invalid", PasswordSha256 = hashes[0] }, token: token).ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == 401) { rejected = true; }
            if (!rejected) throw new InvalidDataException("A stored verifier could be replayed as login material.");
        }
    }
}
