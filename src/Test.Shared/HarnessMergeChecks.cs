namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using Microsoft.Data.Sqlite;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Schema 32 folds CLI harnesses into agents: secrets, arguments, environment, models and role instructions survive.</summary>
    internal static class HarnessMergeChecks
    {
        private const string Seed = """
PRAGMA foreign_keys=OFF;
CREATE TABLE connection_harnesses(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, name TEXT NOT NULL, provider TEXT NOT NULL, executable TEXT NOT NULL, cwd TEXT NOT NULL, credential_id TEXT NOT NULL, enabled INTEGER NOT NULL, revision BIGINT NOT NULL DEFAULT 1, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, UNIQUE(tenant_id,id));
CREATE TABLE connection_arguments(id TEXT PRIMARY KEY, tenant_id TEXT NOT NULL, harness_id TEXT NOT NULL, position INTEGER NOT NULL, value TEXT NOT NULL, active INTEGER NOT NULL DEFAULT 1, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, FOREIGN KEY(tenant_id,harness_id) REFERENCES connection_harnesses(tenant_id,id) ON DELETE CASCADE);
INSERT INTO connection_harnesses VALUES('har_claude','ten_local','Claude (container)','claude','claude','','@secret',1,3,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_harnesses VALUES('har_codex','ten_local','Codex alone','codex','codex','','',1,1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_arguments VALUES('arg_1','ten_local','har_claude',0,'-p',1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_arguments VALUES('arg_2','ten_local','har_claude',1,'Unicode 議論',1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_environment_owners(id,tenant_id,restricted,created_utc,updated_utc) VALUES('har_claude','ten_local',1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_environment_variables(id,tenant_id,owner_id,position,name,created_utc,updated_utc) VALUES('env_1','ten_local','har_claude',0,'FIXTURE_ROUTE','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_models(id,tenant_id,name,endpoint_id,harness_id,model,context_characters,supports_tools,enabled,created_utc,updated_utc) VALUES('mod_sonnet','ten_local','Sonnet via Claude','','har_claude','sonnet',100000,0,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_models(id,tenant_id,name,endpoint_id,harness_id,model,context_characters,supports_tools,enabled,created_utc,updated_utc) VALUES('mod_opus','ten_local','Opus via Claude','','har_claude','opus',262144,1,1,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_model_generation(id,tenant_id,output_bytes,created_utc,updated_utc) VALUES('mod_sonnet','ten_local',2048,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO profiles(id,tenant_id,user_id,name,kind,provider,endpoint,model,executable,cwd,secret_env,timeout_seconds,max_output_bytes,enabled,snapshot,created_utc,updated_utc) VALUES('par_skeptic','ten_local','usr_local','Skeptic','cli','claude','','sonnet','claude','','',120,67108864,1,0,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO connection_profiles(id,tenant_id,model_profile_id,credential_id,role_instructions,context_characters,supports_tools,model_revision,transport_revision,transport_id,created_utc,updated_utc) VALUES('par_skeptic','ten_local','mod_sonnet','','Argue the skeptical side.',262144,1,1,3,'har_claude','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
INSERT INTO profile_arguments(id,tenant_id,profile_id,position,value,created_utc,updated_utc) VALUES('arg_old','ten_local','par_skeptic',0,'--model','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
DELETE FROM schema_migrations WHERE version>=32;
""";

        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-harness-merge-" + Guid.NewGuid().ToString("N"));
            ArgueBotPaths paths = new ArgueBotPaths(home);
            RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true };
            try
            {
                string secret;
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token).ConfigureAwait(false))
                        secret = (await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional Claude login", Kind = "claude_oauth", SecretValue = "fictional-claude-oauth" }, token).ConfigureAwait(false)).Id;
                }
                using (SqliteConnection connection = new SqliteConnection("Data Source=" + paths.DatabaseFile))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = Seed.Replace("@secret", secret, StringComparison.Ordinal);
                        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
                for (int pass = 0; pass < 2; pass++)
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    List<RunnerProfile> agents = await database.Profiles.EnumerateAsync(context, token).ConfigureAwait(false);
                    RunnerProfile skeptic = agents.Single(agent => agent.Id == "par_skeptic");
                    Require(skeptic.ModelProfileId.Length == 0 && skeptic.Kind == "cli" && skeptic.Provider == "claude" && skeptic.Executable == "claude" && skeptic.Model == "sonnet", "An agent on a harness model must become a CLI agent with that model.");
                    Require(skeptic.CredentialId == secret && skeptic.RoleInstructions == "Argue the skeptical side." && skeptic.TimeoutSeconds == 120, "The agent must keep its role instructions and timeout and gain the harness secret.");
                    Require(skeptic.Arguments.SequenceEqual(new[] { "-p", "Unicode 議論" }), "The agent must take the harness arguments without a stale --model.");
                    Require(skeptic.RestrictEnvironment && skeptic.EnvironmentVariables.SequenceEqual(new[] { "FIXTURE_ROUTE" }), "The agent must keep the harness environment names.");
                    Require(skeptic.ContextMaxCharacters == 100000 && !skeptic.SupportsTools && skeptic.MaxOutputBytes == 2048, "The agent must keep the model's context, tool and output settings.");
                    RunnerProfile opus = agents.Single(agent => agent.Id == "par_mod_opus");
                    Require(opus.Name == "Opus via Claude" && opus.Model == "opus" && opus.CredentialId == secret && opus.Arguments.SequenceEqual(new[] { "-p", "Unicode 議論" }), "An unused harness model must become its own agent.");
                    RunnerProfile codex = agents.Single(agent => agent.Id == "par_mod_har_codex");
                    Require(codex.Name == "Codex alone" && codex.Provider == "codex" && codex.Model.Length == 0 && codex.CredentialId.Length == 0, "A harness without models must become an agent.");
                    Require(agents.Count == 3, "The migration must not duplicate agents.");
                    Require((await database.Catalog.ModelsAsync(context, token).ConfigureAwait(false)).Count == 0, "CLI models must be folded into agents.");
                    Require(CliModelArguments.Apply(skeptic).SequenceEqual(new[] { "-p", "Unicode 議論", "--model", "sonnet" }), "The agent's model must reach Claude Code as --model.");
                }
                using (SqliteConnection connection = new SqliteConnection("Data Source=" + paths.DatabaseFile))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('connection_harnesses','connection_arguments')";
                        Require(Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0, "The retired harness tables must be dropped.");
                    }
                }
            }
            finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
