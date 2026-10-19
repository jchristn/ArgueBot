namespace ArgueBot.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.Sqlite.SqliteDatabaseDriver;

    /// <summary>Tenant and user-scoped runner profiles with ordered relational arguments.</summary>
    public sealed class ProfileMethods
    {
        private readonly SqliteDatabaseDriver _Database;
        internal ProfileMethods(SqliteDatabaseDriver database) { _Database = database; }

        /// <summary>Create a profile after validating protocol and resource limits.</summary>
        public async Task<RunnerProfile> CreateAsync(RequestContext context, RunnerProfile profile, CancellationToken token = default)
        {
            RequireWrite(context); Validate(profile);
            profile.Id = Ids.New("par_");
            await _Database.WriteAsync((connection, transaction) => InsertAsync(connection, transaction, context, profile, false, token), token).ConfigureAwait(false);
            return profile;
        }

        /// <summary>List enabled or disabled current profiles. Historical snapshots remain private to their runs.</summary>
        public async Task<List<RunnerProfile>> EnumerateAsync(RequestContext context, CancellationToken token = default)
        {
            List<RunnerProfile> profiles = new List<RunnerProfile>();
            using (SqliteConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (SqliteCommand command = Command(connection, null, "SELECT * FROM profiles WHERE tenant_id=@tenant AND user_id=@user AND snapshot=0 ORDER BY name,id", P("tenant", context.TenantId, "user", context.UserId)))
                using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) profiles.Add(Read(reader)); }
                foreach (RunnerProfile profile in profiles) await LoadArgumentsAsync(connection, null, profile, token).ConfigureAwait(false);
            }
            return profiles;
        }

        /// <summary>Read a profile in the caller's ownership scope.</summary>
        public async Task<RunnerProfile> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            using (SqliteConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                RunnerProfile profile;
                using (SqliteCommand command = Command(connection, null, "SELECT * FROM profiles WHERE tenant_id=@tenant AND user_id=@user AND id=@id", P("tenant", context.TenantId, "user", context.UserId, "id", id)))
                using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Participant profile not found."); profile = Read(reader); }
                await LoadArgumentsAsync(connection, null, profile, token).ConfigureAwait(false); return profile;
            }
        }

        internal static void Validate(RunnerProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 200) throw new DomainException(400, "invalid_profile", "Profile name must be 1 to 200 characters.");
            if (profile.TimeoutSeconds < 1 || profile.TimeoutSeconds > 3600 || profile.MaxOutputBytes < 1024 || profile.MaxOutputBytes > 16777216) throw new DomainException(400, "invalid_limits", "Timeout must be 1 to 3600 seconds and output 1 KiB to 16 MiB.");
            if (profile.Kind == "cli")
            {
                if (string.IsNullOrWhiteSpace(profile.Executable)) throw new DomainException(400, "invalid_executable", "A direct executable is required.");
                if (profile.Executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || profile.Executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) throw new DomainException(400, "shell_wrapper", "Configure the underlying executable or script host instead of a shell wrapper.");
                if (profile.WorkingDirectory.Length > 0 && !Directory.Exists(profile.WorkingDirectory)) throw new DomainException(400, "invalid_directory", "Working directory does not exist on the server.");
                if (profile.Provider != "plain" && profile.Provider != "claude" && profile.Provider != "codex" && profile.Provider != "gemini") throw new DomainException(400, "invalid_protocol", "CLI protocol must be plain, claude, codex or gemini.");
            }
            else if (profile.Kind == "rest")
            {
                if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out Uri? endpoint) || (endpoint.Scheme != "http" && endpoint.Scheme != "https") || !string.IsNullOrEmpty(endpoint.UserInfo)) throw new DomainException(400, "invalid_endpoint", "Use an absolute HTTP(S) endpoint without embedded credentials.");
                if (string.IsNullOrWhiteSpace(profile.Model)) throw new DomainException(400, "invalid_model", "A model identifier is required.");
            }
            else throw new DomainException(400, "invalid_runner", "Runner kind must be cli or rest.");
        }

        internal static async Task InsertAsync(SqliteConnection connection, SqliteTransaction transaction, RequestContext context, RunnerProfile profile, bool snapshot, CancellationToken token)
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO profiles(id,tenant_id,user_id,name,kind,provider,endpoint,model,executable,cwd,secret_env,timeout_seconds,max_output_bytes,enabled,snapshot,created_utc,updated_utc) VALUES(@id,@tenant,@user,@name,@kind,@provider,@endpoint,@model,@exe,@cwd,@secret,@timeout,@max,@enabled,@snapshot,@now,@now)", token, P("id", profile.Id, "tenant", context.TenantId, "user", context.UserId, "name", profile.Name, "kind", profile.Kind, "provider", profile.Provider, "endpoint", profile.Endpoint, "model", profile.Model, "exe", profile.Executable, "cwd", profile.WorkingDirectory, "secret", profile.SecretEnvironmentVariable, "timeout", profile.TimeoutSeconds, "max", profile.MaxOutputBytes, "enabled", profile.Enabled ? 1 : 0, "snapshot", snapshot ? 1 : 0)).ConfigureAwait(false);
            for (int index = 0; index < profile.Arguments.Count; index++) await ExecuteAsync(connection, transaction, "INSERT INTO profile_arguments(id,tenant_id,profile_id,position,value,created_utc,updated_utc) VALUES(@id,@tenant,@profile,@position,@value,@now,@now)", token, P("id", Ids.New("arg_"), "tenant", context.TenantId, "profile", profile.Id, "position", index, "value", profile.Arguments[index])).ConfigureAwait(false);
        }
        internal static RunnerProfile Read(SqliteDataReader reader)
        {
            return new RunnerProfile { Id = S(reader, "id"), Name = S(reader, "name"), Kind = S(reader, "kind"), Provider = S(reader, "provider"), Endpoint = S(reader, "endpoint"), Model = S(reader, "model"), Executable = S(reader, "executable"), WorkingDirectory = S(reader, "cwd"), SecretEnvironmentVariable = S(reader, "secret_env"), TimeoutSeconds = reader.GetInt32(reader.GetOrdinal("timeout_seconds")), MaxOutputBytes = reader.GetInt32(reader.GetOrdinal("max_output_bytes")), Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) != 0 };
        }
        internal static async Task LoadArgumentsAsync(SqliteConnection connection, SqliteTransaction? transaction, RunnerProfile profile, CancellationToken token)
        {
            using (SqliteCommand command = Command(connection, transaction, "SELECT value FROM profile_arguments WHERE profile_id=@id ORDER BY position", P("id", profile.Id)))
            using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) profile.Arguments.Add(reader.GetString(0)); }
        }
    }
}
