namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Bearer, header and query HTTP credentials carry their own placement, and endpoints follow it.</summary>
    internal static class TypedCredentialChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-typed-credentials-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token).ConfigureAwait(false))
                    {
                        CredentialMetadata bearer = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional bearer", Kind = "http_bearer", SecretValue = "fictional-bearer-1" }, token).ConfigureAwait(false);
                        CredentialMetadata header = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional header", Kind = "http_header", ParameterName = "x-api-key", SecretValue = "fictional-header-2" }, token).ConfigureAwait(false);
                        CredentialMetadata query = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional query", Kind = "http_query", ParameterName = "key", SecretValue = "fictional-query-3" }, token).ConfigureAwait(false);
                        List<CredentialMetadata> listed = await database.Credentials.EnumerateAsync(context, token).ConfigureAwait(false);
                        Require(listed.Single(item => item.Id == header.Id).ParameterName == "x-api-key" && listed.Single(item => item.Id == query.Id).ParameterName == "key" && listed.Single(item => item.Id == bearer.Id).ParameterName.Length == 0, "Typed credentials must list their parameter names.");
                        Require(!JsonSerializer.Serialize(listed).Contains("fictional-", StringComparison.Ordinal), "Typed credential metadata must stay write-only.");

                        await RejectAsync(() => vault.SaveAsync(context, "", new CredentialRequest { Name = "No name", Kind = "http_header", SecretValue = "fictional" }, token), "invalid_credential", "parameterName", "A header credential needs a header name.").ConfigureAwait(false);
                        await RejectAsync(() => vault.SaveAsync(context, "", new CredentialRequest { Name = "Bad name", Kind = "http_query", ParameterName = "has space", SecretValue = "fictional" }, token), "invalid_credential", "parameterName", "A query name with whitespace must be rejected.").ConfigureAwait(false);
                        await RejectAsync(() => vault.SaveAsync(context, "", new CredentialRequest { Name = "Protected", Kind = "http_header", ParameterName = "Host", SecretValue = "fictional" }, token), "invalid_credential", "parameterName", "Protocol-controlled headers cannot be credential names.").ConfigureAwait(false);
                        await RejectAsync(() => vault.SaveAsync(context, "", new CredentialRequest { Name = "Stray name", Kind = "http_bearer", ParameterName = "x-api-key", SecretValue = "fictional" }, token), "invalid_credential", "parameterName", "A bearer credential cannot carry a parameter name.").ConfigureAwait(false);
                        await RejectAsync(() => vault.SaveAsync(context, header.Id, new CredentialRequest { Name = header.Name, Kind = "http_header", ParameterName = "api-key", Revision = header.Revision, SecretValue = "fictional-header-4" }, token), "credential_changed", "", "A replacement must not change the header name.").ConfigureAwait(false);
                        CredentialMetadata replaced = await vault.SaveAsync(context, header.Id, new CredentialRequest { Name = header.Name, Kind = "http_header", ParameterName = "x-api-key", Revision = header.Revision, SecretValue = "fictional-header-5" }, token).ConfigureAwait(false);
                        Require(replaced.Revision == 2 && await vault.ResolveAsync(context, header.Id, token).ConfigureAwait(false) == "fictional-header-5", "Replacing a typed credential's value must keep its placement.");

                        // Bindings deliberately name the wrong placement; the service takes it from each credential.
                        EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile
                        {
                            Name = "Typed gateway",
                            Protocol = "openai",
                            BaseUrl = "http://127.0.0.1:4491",
                            AuthBindings = new List<HttpAuthBinding>
                            {
                                new HttpAuthBinding { Kind = "query", Name = "wrong", CredentialId = bearer.Id },
                                new HttpAuthBinding { Kind = "bearer", CredentialId = header.Id },
                                new HttpAuthBinding { Kind = "header", Name = "x-other", CredentialId = query.Id },
                            },
                        }, token).ConfigureAwait(false);
                        EndpointProfile stored = (await database.Catalog.EndpointsAsync(context, token, endpoint.Id).ConfigureAwait(false)).Single();
                        Require(stored.AuthBindings.Select(item => item.Kind + ":" + item.Name).SequenceEqual(new[] { "bearer:Authorization", "header:x-api-key", "query:key" }), "Endpoint bindings must follow each typed credential's placement: " + string.Join(", ", stored.AuthBindings.Select(item => item.Kind + ":" + item.Name)));
                        EndpointPreview preview = EndpointRequests.Preview(stored);
                        Require(preview.HeaderNames.SequenceEqual(new[] { "Authorization", "x-api-key" }) && preview.QueryNames.SequenceEqual(new[] { "key" }), "Request previews must show typed credential placements.");

                        CredentialMetadata claude = await vault.SaveAsync(context, "", new CredentialRequest { Name = "Fictional Claude", Kind = "claude_oauth", SecretValue = "fictional-claude" }, token).ConfigureAwait(false);
                        await RejectAsync(() => database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "Wrong kind", Protocol = "openai", BaseUrl = "http://127.0.0.1:4491", AuthBindings = new List<HttpAuthBinding> { new HttpAuthBinding { Kind = "bearer", CredentialId = claude.Id } } }, token), "credential_kind_mismatch", "", "A harness login must not authenticate an endpoint.").ConfigureAwait(false);
                        await RejectAsync(() => database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "Missing", Protocol = "openai", BaseUrl = "http://127.0.0.1:4491", AuthBindings = new List<HttpAuthBinding> { new HttpAuthBinding { Kind = "bearer", CredentialId = "sec_missing" } } }, token), "invalid_configuration", "", "A binding to a missing credential must be rejected.").ConfigureAwait(false);
                    }
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        private static async Task RejectAsync<T>(Func<Task<T>> action, string code, string field, string message)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException failure)
            {
                Require(failure.Code == code && (field.Length == 0 || failure.Field == field), message + " Got " + failure.Code + " (" + failure.Field + ").");
                return;
            }
            throw new InvalidDataException(message);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
