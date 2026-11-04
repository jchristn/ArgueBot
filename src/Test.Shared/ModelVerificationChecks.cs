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

    internal static class ModelVerificationChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-model-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token).ConfigureAwait(false))
                    await using (ModelCheckService service = new ModelCheckService(database, new CredentialRunner(true, vault, database, context, Path.Combine(home, "runtime"))))
                    {
                        List<ModelProfile> models = new List<ModelProfile>();
                        foreach (string protocol in new[] { "ollama", "openai" })
                        {
                            EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = protocol, Protocol = protocol, BaseUrl = Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL")! }, token).ConfigureAwait(false);
                            models.Add(await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = protocol, EndpointId = endpoint.Id, Model = "check-tools" }, token).ConfigureAwait(false));
                            ModelProfile refused = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "No probe", EndpointId = endpoint.Id, Model = "check-refuses-tools" }, token).ConfigureAwait(false);
                            ModelCheck incomplete = await service.CheckAsync(context, refused.Id, new ModelCheckRequest { IdempotencyKey = "missing-probe", CheckTools = true }, token).ConfigureAwait(false);
                            if (incomplete.State != "failed" || !incomplete.CompletionVerified || incomplete.ToolsVerified || incomplete.ErrorCode != "tool_probe_not_observed") throw new InvalidDataException("A plain completion was incorrectly certified as a tool round trip.");
                        }
                        HarnessProfile harness = await database.Catalog.SaveHarnessAsync(context, new HarnessProfile { Name = "Fixture harness", Provider = "plain", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, "model-check" } }, token).ConfigureAwait(false);
                        models.Add(await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "CLI fixture", HarnessId = harness.Id }, token).ConfigureAwait(false));
                        foreach (ModelProfile model in models)
                        {
                            ModelCheckRequest request = new ModelCheckRequest { IdempotencyKey = "same-check", CheckTools = true };
                            ModelCheck[] same = await Task.WhenAll(service.CheckAsync(context, model.Id, request, token), service.CheckAsync(context, model.Id, request, token)).ConfigureAwait(false);
                            if (same[0].Id != same[1].Id) throw new InvalidDataException("Concurrent check retry created another invocation.");
                            ModelCheck check = (await database.ModelChecks.EnumerateAsync(context, model.Id, token: token).ConfigureAwait(false)).Single();
                            if (check.State != "completed" || !check.ToolsVerified || !check.CompletionVerified || check.ModelRevision != model.Revision || check.TransportRevision != 1) throw new InvalidDataException("Successful model verification lost evidence: " + model.Name + " / " + check.ErrorCode);
                            model.Enabled = false; await database.Catalog.SaveModelAsync(context, model, token).ConfigureAwait(false);
                            ModelCheck duplicate = await service.CheckAsync(context, model.Id, request, token).ConfigureAwait(false);
                            if (duplicate.Id != check.Id || duplicate.ModelRevision != 1) throw new InvalidDataException("Check retry re-resolved changed configuration.");
                            try { await service.CheckAsync(context, model.Id, new ModelCheckRequest { IdempotencyKey = request.IdempotencyKey }, token).ConfigureAwait(false); throw new InvalidDataException("Check retry accepted changed input."); }
                            catch (DomainException failure) when (failure.Code == "idempotency_conflict") { }
                        }
                        ModelProfile uncertainModel = models[0]; uncertainModel.Enabled = true; await database.Catalog.SaveModelAsync(context, uncertainModel, token).ConfigureAwait(false);
                        ModelCheck uncertain = await database.ModelChecks.CreateAsync(context, uncertainModel.Id, new ModelCheckRequest { IdempotencyKey = "uncertain" }, token).ConfigureAwait(false);
                        await database.ModelChecks.ReconcileAsync(token).ConfigureAwait(false);
                        ModelCheck recovered = await service.CheckAsync(context, uncertainModel.Id, new ModelCheckRequest { IdempotencyKey = "uncertain" }, token).ConfigureAwait(false);
                        if (recovered.Id != uncertain.Id || recovered.State != "uncertain" || recovered.CompletionVerified) throw new InvalidDataException("Service recovery replayed an uncertain model check.");
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
    }
}
