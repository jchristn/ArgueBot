namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class AuthenticatedPanelChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-auth-transport-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token).ConfigureAwait(false))
                    {
                        CredentialMetadata credential = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional transport", SecretValue = "fictional-gateway-token" }, token).ConfigureAwait(false);
                        CredentialRunner runner = new CredentialRunner(false, vault, database, context, Path.Combine(home, "runtime"));
                        foreach (string protocol in new[] { "ollama", "openai" })
                        {
                            EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = protocol, Protocol = protocol, BaseUrl = Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL")!, TimeoutSeconds = 10, AuthBindings = new List<HttpAuthBinding> { new HttpAuthBinding { Kind = "bearer", CredentialId = credential.Id }, new HttpAuthBinding { Kind = "api_key", Name = "X-Api-Key", CredentialId = credential.Id }, new HttpAuthBinding { Kind = "header", Name = "X-Route", Value = "fixture" }, new HttpAuthBinding { Kind = "query", Name = "gateway_key", CredentialId = credential.Id } } }, token).ConfigureAwait(false);
                            EndpointDiscovery discovery = await EndpointDiscoveryService.DiscoverAsync(endpoint, vault, context, token).ConfigureAwait(false);
                            if (discovery.Models.Count != 2 || !discovery.Models.Contains("multi-agent") || discovery.Revision != endpoint.Revision) throw new InvalidDataException("Explicit model discovery failed.");
                            string baseUrl = endpoint.BaseUrl; endpoint.BaseUrl += "/unavailable";
                            try { await EndpointDiscoveryService.DiscoverAsync(endpoint, vault, context, token).ConfigureAwait(false); throw new InvalidDataException("Discovery unexpectedly accepted a rejected credential."); }
                            catch (DomainException failure) when (failure.Code == "discovery_failed" && !failure.Message.Contains("fictional-gateway-token", StringComparison.Ordinal)) { }
                            endpoint.BaseUrl = baseUrl;
                            ModelProfile model = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = protocol, EndpointId = endpoint.Id, Model = "multi-agent-auth", MaxOutputBytes = 1048576, Generation = new ModelGenerationSettings { MaxOutputTokens = 2048, Temperature = 0.25, TopP = 0.9 } }, token).ConfigureAwait(false);
                            List<string> ids = new List<string>();
                            for (int index = 0; index < 4; index++) ids.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = protocol + index, ModelProfileId = model.Id }, token).ConfigureAwait(false)).Id);
                            Conversation conversation = await database.CreateConversationAsync(context, "Authenticated " + protocol, token).ConfigureAwait(false);
                            for (int index = 0; index < 20; index++) await database.AppendMessageAsync(context, new Message { ConversationId = conversation.Id, Content = "Transport framing evidence " + index + new string('x', 1200) }, token: token).ConfigureAwait(false);
                            await using (ConversationEngine engine = new ConversationEngine(database, runner, contextMaxCharacters: 16000))
                            {
                                DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = ids[0], ParticipantIds = ids, MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "auth" }, token).ConfigureAwait(false);
                                for (int index = 0; index < 500; index++)
                                {
                                    run = await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                                    if (run.State == "completed" || run.State == "paused") break;
                                    await Task.Delay(20, token).ConfigureAwait(false);
                                }
                                if (run.Outcome != "consensus") throw new InvalidDataException("Authenticated " + protocol + " did not reach unanimous agreement: " + run.Outcome);
                                AttemptPage provenance = await database.Attempts.EnumerateAsync(context, conversation.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                                if (!provenance.Items.Any(attempt => attempt.Purpose == "compaction")) throw new InvalidDataException("Transport-aware context preparation did not compact oversized framed requests.");
                                if (provenance.Items.Any(attempt => attempt.CredentialRevisions.Count != 1 || attempt.CredentialRevisions[0].CredentialId != credential.Id || attempt.CredentialRevisions[0].Revision != credential.Revision)) throw new InvalidDataException("Invocation provenance did not retain the exact resolved credential revision.");
                                List<DebateProposal> proposals = await database.Debates.ReadProposalsAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                                if (proposals.Single().Agreements.Count != 4) throw new InvalidDataException("The transport bypassed individual ratification.");
                                MessagePage messages = await database.ReadMessagePageAsync(context, conversation.Id, pageSize: 100, token: token).ConfigureAwait(false);
                                if (!messages.Items.Any(item => item.Kind == "tool") || messages.Items.Any(item => item.Content.Contains("fictional-gateway-token", StringComparison.Ordinal))) throw new InvalidDataException("Conversation tools or credential redaction failed.");
                            }
                            RunnerProfile capacityProfile = await database.Profiles.ReadAsync(context, ids[0], token).ConfigureAwait(false);
                            await database.Catalog.ResolveAsync(context, capacityProfile, token).ConfigureAwait(false);
                            capacityProfile.Model = "capacity-fixture";
                            for (int limit = 1; limit <= 2; limit++)
                            {
                                endpoint.MaxConcurrentInvocations = limit;
                                endpoint = await database.Catalog.SaveEndpointAsync(context, endpoint, token).ConfigureAwait(false);
                                int active = 0; int maximum = 0;
                                object measurement = new object();
                                await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
                                {
                                    await runner.GenerateAsync(capacityProfile, "Capacity fixture", new List<Message>(), async (text, cancellation) =>
                                    {
                                        lock (measurement) { active++; maximum = Math.Max(maximum, active); }
                                        try { await Task.Delay(80, cancellation).ConfigureAwait(false); }
                                        finally { lock (measurement) { active--; } }
                                    }, token).ConfigureAwait(false);
                                })).ConfigureAwait(false);
                                if (maximum != limit) throw new InvalidDataException("Shared endpoint capacity failed for " + protocol + ": " + maximum + "/" + limit);
                            }
                            endpoint.MaxConcurrentInvocations = 1;
                            await database.Catalog.SaveEndpointAsync(context, endpoint, token).ConfigureAwait(false);
                            TaskCompletionSource<bool> entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            Task held = runner.GenerateAsync(capacityProfile, "Hold lease", new List<Message>(), async (_, cancellation) => { entered.TrySetResult(true); await release.Task.WaitAsync(cancellation).ConfigureAwait(false); }, token);
                            try
                            {
                                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                                using (CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(token))
                                {
                                    Task cancelled = runner.GenerateAsync(capacityProfile, "Cancel queued work", new List<Message>(), (_, _) => throw new InvalidDataException("Cancelled queued work reached the provider."), waiting.Token);
                                    waiting.CancelAfter(50);
                                    try { await cancelled.ConfigureAwait(false); throw new InvalidDataException("Queued cancellation was ignored."); }
                                    catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
                                }
                            }
                            finally { release.TrySetResult(true); await held.ConfigureAwait(false); }
                            await runner.GenerateAsync(capacityProfile, "Lease recovered", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false);
                        }
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
