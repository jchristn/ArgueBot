namespace Test.Shared
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class PanelProviderChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, string home, CancellationToken token)
        {
            RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
            EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "Generation storage", BaseUrl = "http://127.0.0.1:11434" }, token).ConfigureAwait(false);
            ModelProfile httpModel = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "Generation settings", EndpointId = endpoint.Id, Model = "fictional", MaxOutputBytes = 131072, Generation = new ModelGenerationSettings { MaxOutputTokens = 1024, Temperature = 0.1234, TopP = 0.9876 } }, token).ConfigureAwait(false);
            RunnerProfile resolved = new RunnerProfile { Name = "Generation storage profile", ModelProfileId = httpModel.Id };
            await database.Catalog.ResolveAsync(context, resolved, token).ConfigureAwait(false);
            RunnerProfile stored = await database.Profiles.CreateAsync(context, resolved, token).ConfigureAwait(false);
            stored = await database.Profiles.ReadAsync(context, stored.Id, token).ConfigureAwait(false);
            if (stored.Generation.Temperature != 0.1234 || stored.Generation.TopP != 0.9876 || stored.Generation.MaxOutputTokens != 1024 || stored.MaxOutputBytes != 131072) throw new InvalidDataException("Provider changed generation settings or their precision.");
            httpModel.Generation.Temperature = null; httpModel.Generation.TopP = null; httpModel.Generation.MaxOutputTokens = null;
            await database.Catalog.SaveModelAsync(context, httpModel, token).ConfigureAwait(false);
            httpModel = (await database.Catalog.ModelsAsync(context, token, httpModel.Id).ConfigureAwait(false)).Single();
            if (httpModel.Generation.Temperature.HasValue || httpModel.Generation.TopP.HasValue || httpModel.Generation.MaxOutputTokens.HasValue) throw new InvalidDataException("Clearing generation overrides did not preserve adapter defaults.");
            List<string> ids = new List<string>();
            for (int index = 0; index < 5; index++) ids.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Agent " + index, Kind = "cli", Provider = "plain", Executable = "fictional" }, token).ConfigureAwait(false)).Id);
            Conversation conversation = await database.CreateConversationAsync(context, "Provider explicit consensus", token).ConfigureAwait(false);
            await using (ConversationEngine engine = new ConversationEngine(database, new MultiAgentFixtureRunner()))
            {
                DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, ParticipantIds = ids.Take(4).ToList(), OrchestratorId = ids[4], MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "provider-panel" }, token).ConfigureAwait(false);
                for (int attempt = 0; attempt < 1000 && run.State != "completed" && run.State != "paused"; attempt++)
                { await Task.Delay(10, token).ConfigureAwait(false); run = await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false); }
                List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                if (run.Outcome != "consensus" || proposals.Single().Agreements.Count != 4) throw new InvalidDataException("Provider did not retain four explicit votes: " + run.Outcome);
            }
            using (CredentialVault vault = await CredentialVault.OpenAsync(database, home, context, token).ConfigureAwait(false))
            {
                CredentialMetadata credential = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Provider fictional token", SecretValue = "fictional-provider-credential" }, token).ConfigureAwait(false);
                if (await vault.ResolveAsync(context, credential.Id, token).ConfigureAwait(false) != "fictional-provider-credential") throw new InvalidDataException("Provider credential ciphertext failed to round trip.");
                CredentialKeyStatus key = await vault.KeyStatusAsync(token).ConfigureAwait(false);
                CredentialKeyStatus rotated = await vault.RotateKeyAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = key.ActiveKeyId }, token).ConfigureAwait(false);
                if (rotated.ReencryptedCredentials < 1 || await vault.ResolveAsync(context, credential.Id, token).ConfigureAwait(false) != "fictional-provider-credential") throw new InvalidDataException("Provider master-key rotation failed.");
                await database.Credentials.DisableAsync(context, credential.Id, credential.Revision, token).ConfigureAwait(false);
            }
        }
    }
}
