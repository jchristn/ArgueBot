namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>CLI agents carry their optional secret and model into a debate; an HTTP model can be seated directly as a shortcut.</summary>
    internal static class SeatSelectionChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-seats-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true };
                    using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token).ConfigureAwait(false))
                    {
                        CredentialMetadata secret = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional Claude login", Kind = "claude_oauth", SecretValue = "fictional-claude-oauth" }, token).ConfigureAwait(false);
                        CredentialMetadata codex = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional Codex login", Kind = "codex_auth_json", SecretValue = "{\"tokens\":{\"access_token\":\"fictional\"}}" }, token).ConfigureAwait(false);
                        RunnerProfile claude = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Claude (container)", Kind = "cli", Provider = "claude", Executable = "claude", CredentialId = secret.Id, Model = "sonnet", RoleInstructions = "Argue the skeptical side." }, token).ConfigureAwait(false);
                        RunnerProfile second = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Claude second seat", Kind = "cli", Provider = "claude", Executable = "claude", CredentialId = secret.Id }, token).ConfigureAwait(false);
                        EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "Local fixture", Protocol = "ollama", BaseUrl = "http://127.0.0.1:9" }, token).ConfigureAwait(false);
                        ModelProfile model = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "Fixture HTTP model", EndpointId = endpoint.Id, Model = "fixture" }, token).ConfigureAwait(false);
                        Conversation conversation = await database.CreateConversationAsync(context, "Seat fixture", token).ConfigureAwait(false);
                        DebateRun run = await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, Mode = "debate", Question = "Do seats carry their sign-in?", ParticipantIds = new List<string> { claude.Id, model.Id }, OrchestratorId = second.Id, IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                        RunnerProfile fromAgent = run.Participants.Single(seat => seat.Name == "Claude (container)");
                        RunnerProfile fromModel = run.Participants.Single(seat => seat.Name == "Fixture HTTP model");
                        Require(fromAgent.Kind == "cli" && fromAgent.CredentialId == secret.Id && fromAgent.Executable == "claude" && fromAgent.Model == "sonnet" && fromAgent.RoleInstructions == "Argue the skeptical side.", "A CLI agent seat must carry its secret, model and role instructions.");
                        List<string> arguments = CliModelArguments.Apply(fromAgent);
                        Require(arguments.Count >= 2 && arguments[arguments.Count - 2] == "--model" && arguments[arguments.Count - 1] == "sonnet", "A Claude Code agent's model must reach the program as --model.");
                        Require(fromModel.Kind == "rest" && fromModel.ModelProfileId == model.Id && fromModel.TransportId == endpoint.Id && fromModel.CredentialId.Length == 0, "A model shortcut seat must run on its endpoint.");
                        Require(run.Orchestrator != null && run.Orchestrator.CredentialId == secret.Id, "A CLI agent orchestrator must carry its secret.");
                        RunnerProfile plain = new RunnerProfile { Kind = "cli", Provider = "plain", Executable = "fixture", Arguments = new List<string> { "--use", "{model}" }, Model = "fixture-model" };
                        Require(CliModelArguments.Apply(plain).SequenceEqual(new[] { "--use", "fixture-model" }), "A {model} argument must take the agent's model.");
                        await Rejects(() => database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Wrong secret", Kind = "cli", Provider = "claude", Executable = "claude", CredentialId = codex.Id }, token), "credential_kind_mismatch").ConfigureAwait(false);
                        await Rejects(() => database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Plain secret", Kind = "cli", Provider = "plain", Executable = "fixture", CredentialId = secret.Id }, token), "invalid_secret").ConfigureAwait(false);
                        await Rejects(() => database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Missing secret", Kind = "cli", Provider = "claude", Executable = "claude", CredentialId = "crd_missing" }, token), "invalid_secret").ConfigureAwait(false);
                        await Rejects(() => database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Plain model", Kind = "cli", Provider = "plain", Executable = "fixture", Model = "fixture-model" }, token), "model_flag_required").ConfigureAwait(false);
                        RunnerProfile signedInLocally = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Local login", Kind = "cli", Provider = "codex", Executable = "codex" }, token).ConfigureAwait(false);
                        Require(signedInLocally.CredentialId.Length == 0, "A secret must stay optional.");
                        try
                        {
                            await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, Mode = "debate", Question = "Missing", ParticipantIds = new List<string> { "par_missing", model.Id }, OrchestratorId = second.Id, IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                            throw new InvalidDataException("A missing agent was accepted as a seat.");
                        }
                        catch (DomainException ex) when (ex.Code == "profile_not_found" && ex.Field == "participantIds") { }
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static async Task Rejects(Func<Task> action, string code)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) when (ex.Code == code) { return; }
            throw new InvalidDataException("Expected " + code + ".");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
