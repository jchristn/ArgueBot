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
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class CatalogChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-catalog-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    await HarnessEnvironmentChecks.RunAsync(database, context, token).ConfigureAwait(false);
                    CredentialRefreshHandler handler = new CredentialRefreshHandler();
                    using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token, handler).ConfigureAwait(false))
                    {
                        string runtime = Path.Combine(home, "credential-runtime");
                        string abandoned = Path.Combine(runtime, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(abandoned);
                        await File.WriteAllTextAsync(Path.Combine(abandoned, ".arguebot-credential-home"), "arguebot invocation credential home", token).ConfigureAwait(false);
                        await File.WriteAllTextAsync(Path.Combine(abandoned, "auth.json"), "fictional abandoned login", token).ConfigureAwait(false);
                        string unrelated = Path.Combine(runtime, "operator-files"); Directory.CreateDirectory(unrelated);
                        _ = new CredentialRunner(false, vault, database, context, runtime);
                        Require(!Directory.Exists(abandoned) && Directory.Exists(unrelated), "Startup must clean only marked invocation homes after exclusive ownership is established.");
                        CredentialMetadata credential = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional gateway", SecretValue = "fictional-key-A-123" }, token).ConfigureAwait(false);
                        Require(!JsonSerializer.Serialize(credential).Contains("fictional-key", StringComparison.Ordinal), "Credential metadata must be write-only.");
                        Require(await vault.ResolveAsync(context, credential.Id, token).ConfigureAwait(false) == "fictional-key-A-123", "Encrypted credentials must resolve only within execution.");
                        EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "Gateway", Protocol = "openai", BaseUrl = "http://127.0.0.1:4491", AuthBindings = new List<HttpAuthBinding> { new HttpAuthBinding { Kind = "bearer", CredentialId = credential.Id }, new HttpAuthBinding { Kind = "query", Name = "gateway_key", CredentialId = credential.Id }, new HttpAuthBinding { Kind = "header", Name = "X-Route", Value = "fixture" } } }, token).ConfigureAwait(false);
                        EndpointPreview preview = EndpointRequests.Preview(endpoint);
                        Require(preview.GenerationUrl == "http://127.0.0.1:4491/chat/completions" && preview.DiscoveryUrl == "http://127.0.0.1:4491/models" && preview.HeaderNames.SequenceEqual(new[] { "Authorization", "X-Route" }) && preview.QueryNames.Single() == "gateway_key" && !JsonSerializer.Serialize(preview).Contains("fictional-key", StringComparison.Ordinal), "Preview changed destinations or resolved a credential.");
                        ModelProfile model = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "Fixture model", EndpointId = endpoint.Id, Model = "fictional-chat", MaxOutputBytes = 1048576, Generation = new ModelGenerationSettings { MaxOutputTokens = 2048, Temperature = 0.25, TopP = 0.9 } }, token).ConfigureAwait(false);
                        RunnerProfile participant = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Panel A", ModelProfileId = model.Id }, token).ConfigureAwait(false);
                        RunnerProfile other = await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Panel B", ModelProfileId = model.Id }, token).ConfigureAwait(false);
                        Conversation conversation = await database.CreateConversationAsync(context, "Snapshot", token).ConfigureAwait(false);
                        DebateRun run = await database.Runs.CreateAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = participant.Id, ParticipantIds = new List<string> { participant.Id, other.Id }, IdempotencyKey = "snapshot" }, token).ConfigureAwait(false);
                        model.Generation.Temperature = 1.0; model.Generation.MaxOutputTokens = 8192; model = await database.Catalog.SaveModelAsync(context, model, token).ConfigureAwait(false);
                        endpoint.BaseUrl = "http://127.0.0.1:4492"; endpoint = await database.Catalog.SaveEndpointAsync(context, endpoint, token).ConfigureAwait(false);
                        run = await database.Runs.ReadAsync(context, conversation.Id, run.Id, token).ConfigureAwait(false);
                        Require(run.Participants.All(item => item.Endpoint.EndsWith(":4491", StringComparison.Ordinal) && item.AuthBindings.Count == 3 && item.TransportRevision == 1), "Accepted snapshots must freeze endpoints and authentication references.");
                        Require(run.Participants.All(item => item.MaxOutputBytes == 1048576 && item.Generation.MaxOutputTokens == 2048 && item.Generation.Temperature == 0.25 && item.Generation.TopP == 0.9), "Generation settings changed inside an accepted model snapshot.");
                        CredentialMetadata replacement = await vault.SaveAsync(context, credential.Id, new CredentialRequest { Name = credential.Name, Revision = credential.Revision, SecretValue = "fictional-key-B-456" }, token).ConfigureAwait(false);
                        Require(replacement.Revision == 2 && await vault.ResolveAsync(context, credential.Id, token).ConfigureAwait(false) == "fictional-key-B-456", "Rotation must update the referenced value without changing snapshots.");
                        try { await vault.SaveAsync(context, credential.Id, new CredentialRequest { Name = "Stale", Revision = 1, SecretValue = "fictional-stale" }, token).ConfigureAwait(false); throw new InvalidDataException("Stale credential replacement was accepted."); }
                        catch (DomainException error) when (error.Code == "credential_changed") { }
                        EndpointProfile unsafeEndpoint = new EndpointProfile { Name = "Unsafe", AuthBindings = new List<HttpAuthBinding> { new HttpAuthBinding { Kind = "header", Name = "Host", Value = "127.0.0.1" } } };
                        try { await database.Catalog.SaveEndpointAsync(context, unsafeEndpoint, token).ConfigureAwait(false); throw new InvalidDataException("Unsafe header was accepted."); }
                        catch (DomainException error) when (error.Code == "invalid_configuration") { }
                        string login = "{\"tokens\":{\"access_token\":\"fictional-old-access\",\"refresh_token\":\"fictional-refresh\"},\"last_refresh\":\"2020-01-01T00:00:00Z\"}";
                        CredentialMetadata codex = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Dedicated CLI", Kind = "codex_auth_json", SecretValue = login }, token).ConfigureAwait(false);
                        string[] refreshed = await Task.WhenAll(vault.PrepareCodexAsync(context, codex.Id, token), vault.PrepareCodexAsync(context, codex.Id, token)).ConfigureAwait(false);
                        Require(handler.Calls == 1 && refreshed.All(value => value.Contains("fictional-refreshed-access", StringComparison.Ordinal)), "Concurrent Codex invocations must share one refresh.");
                        using (JsonDocument childLogin = JsonDocument.Parse(CredentialVault.CodexRunCopy(refreshed[0])))
                            Require(childLogin.RootElement.GetProperty("tokens").GetProperty("refresh_token").GetString() == "", "Children require the refresh field for CLI compatibility but must never receive refresh authority.");
                        handler.Reject = true;
                        CredentialMetadata uncertain = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Rejected CLI", Kind = "codex_auth_json", SecretValue = login }, token).ConfigureAwait(false);
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            try { await vault.PrepareCodexAsync(context, uncertain.Id, token).ConfigureAwait(false); throw new InvalidDataException("Rejected refresh unexpectedly succeeded."); }
                            catch (DomainException error) when (error.Code == "reauthentication_required" || error.Code == "credential_unavailable") { }
                        }
                        Require(handler.Calls == 2, "An uncertain refresh must not replay its token.");
                        Require((await database.Credentials.EnumerateAsync(context, token, uncertain.Id).ConfigureAwait(false)).Single().Status == "refresh_uncertain", "A rejected refresh must leave the login marked refresh_uncertain.");
                        handler.Reject = false;
                        CredentialMetadata garbled = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Garbled refresh", Kind = "codex_auth_json", SecretValue = login }, token).ConfigureAwait(false);
                        handler.Failure = "invalid_body";
                        try { await vault.PrepareCodexAsync(context, garbled.Id, token).ConfigureAwait(false); throw new InvalidDataException("An invalid refresh response was accepted."); }
                        catch (DomainException error) when (error.Code == "reauthentication_required") { }
                        Require((await database.Credentials.EnumerateAsync(context, token, garbled.Id).ConfigureAwait(false)).Single().Status == "refresh_uncertain", "An invalid refresh response must leave the login uncertain, because the service may have consumed the refresh token.");
                        handler.Failure = "";
                        foreach (string failure in new[] { "connect", "server_error", "rate_limited" })
                        {
                            CredentialMetadata transient = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Transient " + failure, Kind = "codex_auth_json", SecretValue = login }, token).ConfigureAwait(false);
                            handler.Failure = failure; int calls = handler.Calls;
                            try { await vault.PrepareCodexAsync(context, transient.Id, token).ConfigureAwait(false); throw new InvalidDataException("A failed refresh unexpectedly succeeded: " + failure); }
                            catch (DomainException error) when (error.Code == "refresh_unavailable") { }
                            CredentialMetadata released = (await database.Credentials.EnumerateAsync(context, token, transient.Id).ConfigureAwait(false)).Single();
                            Require(released.Status == "ready" && released.Revision == transient.Revision + 2, "A refresh that did not reach or was refused by the login service must leave the login ready: " + failure + "/" + released.Status);
                            handler.Failure = "";
                            string recovered = await vault.PrepareCodexAsync(context, transient.Id, token).ConfigureAwait(false);
                            Require(handler.Calls == calls + 2 && recovered.Contains("fictional-refreshed-access", StringComparison.Ordinal), "A released login must refresh normally on retry: " + failure);
                        }
                        for (int index = 0; index < 101; index++)
                            await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "A page fixture " + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) }, token).ConfigureAwait(false);
                        List<EndpointProfile> firstPage = await database.Catalog.EndpointsAsync(context, token).ConfigureAwait(false);
                        List<EndpointProfile> secondPage = await database.Catalog.EndpointsAsync(context, token, pageNumber: 2).ConfigureAwait(false);
                        Require(firstPage.Count == 100 && secondPage.Count == 2 && !firstPage.Select(item => item.Id).Intersect(secondPage.Select(item => item.Id)).Any(), "Catalog pages must retain all resources without overlap.");
                        RunnerProfile beyondPage = new RunnerProfile { ModelProfileId = model.Id };
                        await database.Catalog.ResolveAsync(context, beyondPage, token).ConfigureAwait(false);
                        Require(beyondPage.Endpoint == endpoint.BaseUrl, "Snapshot admission must resolve endpoints beyond the first page.");
                        try { await database.Catalog.EndpointsAsync(context, token, pageSize: 101).ConfigureAwait(false); throw new InvalidDataException("Unbounded catalog page was accepted."); }
                        catch (DomainException failure) when (failure.Code == "invalid_page") { }
                        CredentialKeyStatus before = await vault.KeyStatusAsync(token).ConfigureAwait(false);
                        string oldCiphertext = await database.Credentials.ResolveAsync(context, credential.Id, token).ConfigureAwait(false);
                        CredentialKeyStatus rotated = await vault.RotateKeyAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = before.ActiveKeyId }, token).ConfigureAwait(false);
                        Require(rotated.ActiveKeyId != before.ActiveKeyId && rotated.ReencryptedCredentials == 7 && rotated.RetainedKeyCount == 2, "Master rotation lost keys or records.");
                        Require(await vault.ResolveAsync(context, credential.Id, token).ConfigureAwait(false) == "fictional-key-B-456", "Master rotation changed credential values.");
                        CredentialMetadata? used = null;
                        await vault.ResolveAsync(context, credential.Id, token, metadata => used = metadata).ConfigureAwait(false);
                        Require(used?.Revision == replacement.Revision, "Master rotation changed authentication revision.");
                        try { await vault.RotateKeyAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = before.ActiveKeyId }, token).ConfigureAwait(false); throw new InvalidDataException("Stale master rotation was accepted."); }
                        catch (DomainException failure) when (failure.Code == "key_changed") { }
                        // Reproduce a crash after durable key installation but before database re-encryption.
                        await database.Credentials.ReencryptAsync(context, (id, encrypted) => id == credential.Id ? oldCiphertext : encrypted, token).ConfigureAwait(false);
                        using (CredentialVault reopened = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token, new CredentialRefreshHandler()).ConfigureAwait(false))
                        {
                            Require(await reopened.ResolveAsync(context, credential.Id, token).ConfigureAwait(false) == "fictional-key-B-456", "Restart could not read a retained-key record after interrupted re-encryption.");
                            Require(await reopened.ResolveAsync(context, codex.Id, token).ConfigureAwait(false) == refreshed[0], "Restart could not read a newly encrypted record.");
                        }
                        int rewrites = 0;
                        try { await database.Credentials.ReencryptAsync(context, (_, encrypted) => ++rewrites == 2 ? throw new InvalidOperationException("Fixture rollback") : "invalid-fixture", token).ConfigureAwait(false); throw new InvalidDataException("Fixture transaction did not abort."); }
                        catch (InvalidOperationException failure) when (failure.Message == "Fixture rollback") { }
                        Require(await vault.ResolveAsync(context, credential.Id, token).ConfigureAwait(false) == "fictional-key-B-456", "Failed re-encryption committed a partial transaction.");
                        await database.Credentials.DisableAsync(context, credential.Id, replacement.Revision, token).ConfigureAwait(false);
                        try { await vault.ResolveAsync(context, credential.Id, token).ConfigureAwait(false); throw new InvalidDataException("Disabled credential resolved."); }
                        catch (DomainException error) when (error.Code == "credential_unavailable") { }
                        CredentialMetadata corrupted = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Corrupted gateway", SecretValue = "fictional-key-C-789" }, token).ConfigureAwait(false);
                        CredentialKeyStatus beforeCorruption = await vault.KeyStatusAsync(token).ConfigureAwait(false);
                        await database.Credentials.ReencryptAsync(context, (id, encrypted) => id == corrupted.Id ? "v1:" + beforeCorruption.ActiveKeyId + ":" + Convert.ToBase64String(new byte[48]) : encrypted, token).ConfigureAwait(false);
                        try { await vault.RotateKeyAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = beforeCorruption.ActiveKeyId }, token).ConfigureAwait(false); throw new InvalidDataException("Rotation accepted an unreadable enabled credential."); }
                        catch (DomainException failure) when (failure.Code == "credential_unreadable" && failure.Message.Contains("Corrupted gateway", StringComparison.Ordinal)) { }
                        Require((await vault.KeyStatusAsync(token).ConfigureAwait(false)).ActiveKeyId == beforeCorruption.ActiveKeyId, "A refused rotation must not activate a new key.");
                        await database.Credentials.DisableAsync(context, corrupted.Id, corrupted.Revision, token).ConfigureAwait(false);
                        CredentialKeyStatus tolerated = await vault.RotateKeyAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = beforeCorruption.ActiveKeyId }, token).ConfigureAwait(false);
                        Require(tolerated.SkippedCredentials == 1 && tolerated.RetainedKeyCount == 3 && tolerated.ActiveKeyId != beforeCorruption.ActiveKeyId, "Rotation must skip and report an unreadable disabled credential.");
                        try { await vault.RetireKeysAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = tolerated.ActiveKeyId }, token).ConfigureAwait(false); throw new InvalidDataException("Retirement removed a key still referenced by a credential."); }
                        catch (DomainException failure) when (failure.Code == "keys_in_use") { }
                        try { await vault.RetireKeysAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = beforeCorruption.ActiveKeyId }, token).ConfigureAwait(false); throw new InvalidDataException("Stale retirement was accepted."); }
                        catch (DomainException failure) when (failure.Code == "key_changed") { }
                        CredentialMetadata disabledCorrupted = (await database.Credentials.EnumerateAsync(context, token, corrupted.Id).ConfigureAwait(false)).Single();
                        await vault.SaveAsync(context, corrupted.Id, new CredentialRequest { Name = corrupted.Name, Revision = disabledCorrupted.Revision, SecretValue = "fictional-key-D-012" }, token).ConfigureAwait(false);
                        CredentialKeyStatus retired = await vault.RetireKeysAsync(context, new RotateCredentialKeyRequest { ExpectedKeyId = tolerated.ActiveKeyId }, token).ConfigureAwait(false);
                        Require(retired.RetiredKeyCount == 2 && retired.RetainedKeyCount == 1 && retired.ActiveKeyId == tolerated.ActiveKeyId, "Retirement must remove every earlier key once no credential uses one.");
                        using (CredentialVault reopened = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token, new CredentialRefreshHandler()).ConfigureAwait(false))
                        {
                            Require((await reopened.KeyStatusAsync(token).ConfigureAwait(false)).RetainedKeyCount == 1, "Retirement was not durable.");
                            Require(await reopened.ResolveAsync(context, codex.Id, token).ConfigureAwait(false) == refreshed[0], "A credential became unreadable after retirement.");
                        }
                        foreach (string path in Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith("credential.key", StringComparison.Ordinal) && !path.EndsWith(".arguebot-owner", StringComparison.Ordinal)))
                        {
                            string text = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false));
                            Require(!text.Contains("fictional-key-A-123", StringComparison.Ordinal) && !text.Contains("fictional-rotated-refresh", StringComparison.Ordinal), "Stored configuration or logs contained plaintext credentials.");
                        }
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
