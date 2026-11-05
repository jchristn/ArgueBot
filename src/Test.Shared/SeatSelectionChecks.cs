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

    /// <summary>Debates accept models and CLI harnesses as seats, and each seat carries its harness secret into the run snapshot.</summary>
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
                        HarnessProfile harness = await database.Catalog.SaveHarnessAsync(context, new HarnessProfile { Name = "Claude (container)", Provider = "claude", Executable = "claude", CredentialId = secret.Id }, token).ConfigureAwait(false);
                        HarnessProfile second = await database.Catalog.SaveHarnessAsync(context, new HarnessProfile { Name = "Claude second seat", Provider = "claude", Executable = "claude", CredentialId = secret.Id }, token).ConfigureAwait(false);
                        ModelProfile model = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "Sonnet via harness", HarnessId = harness.Id, Model = "sonnet" }, token).ConfigureAwait(false);
                        Conversation conversation = await database.CreateConversationAsync(context, "Seat fixture", token).ConfigureAwait(false);
                        DebateRun run = await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, Mode = "debate", Question = "Do seats carry their sign-in?", ParticipantIds = new List<string> { second.Id, model.Id }, OrchestratorId = harness.Id, IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                        RunnerProfile fromHarness = run.Participants.Single(seat => seat.Name == "Claude second seat");
                        RunnerProfile fromModel = run.Participants.Single(seat => seat.Name == "Sonnet via harness");
                        Require(fromHarness.Kind == "cli" && fromHarness.CredentialId == secret.Id && fromHarness.TransportId == second.Id && fromHarness.Executable == "claude", "A CLI harness seat must carry its harness and secret.");
                        Require(fromModel.Kind == "cli" && fromModel.CredentialId == secret.Id && fromModel.TransportId == harness.Id && fromModel.ModelProfileId == model.Id, "A model seat must carry its harness and secret.");
                        Require(run.Orchestrator != null && run.Orchestrator.CredentialId == secret.Id, "A CLI harness orchestrator must carry its secret.");
                        try
                        {
                            await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, Mode = "debate", Question = "Missing", ParticipantIds = new List<string> { "har_missing", model.Id }, OrchestratorId = harness.Id, IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                            throw new InvalidDataException("A missing harness was accepted as a seat.");
                        }
                        catch (DomainException ex) when (ex.Code == "profile_not_found" && ex.Field == "participantIds") { }
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
