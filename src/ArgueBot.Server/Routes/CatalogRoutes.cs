namespace ArgueBot.Server.Routes
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using System.Globalization;
    using System.Linq;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class CatalogRoutes
    {
        private readonly DatabaseDriverBase _Database;
        private readonly CredentialVault _Vault;
        private readonly ModelCheckService _Checks;
        internal CatalogRoutes(DatabaseDriverBase database, CredentialVault vault, ModelCheckService checks) { _Database = database; _Vault = vault; _Checks = checks; }
        internal void Register(Webserver server)
        {
            const string root = "/v2.0/api/configuration";
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/endpoints/{id}/preview", http => Api.GuardAsync(http, async () =>
            {
                EndpointProfile endpoint = (await _Database.Catalog.EndpointsAsync(Api.Context(http), http.Token, Api.Parameter(http, "id")).ConfigureAwait(false)).SingleOrDefault() ?? throw new DomainException(404, "endpoint_not_found", "The endpoint does not exist.");
                await Api.SendAsync(http, EndpointRequests.Preview(endpoint)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Preview saved request destinations and redacted bindings without contacting the endpoint", "Configuration"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/models/{id}/checks", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.ModelChecks.EnumerateAsync(Api.Context(http), Api.Parameter(http, "id"), int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read observed completion and tool checks for frozen revisions", "Configuration"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/models/{id}/checks", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Checks.CheckAsync(Api.Context(http), Api.Parameter(http, "id"), Api.Body<ModelCheckRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Explicitly invoke a potentially billable model check", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root + "/credential-keys", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Vault.KeyStatusAsync(http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read safe master-key identities", "Credentials"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, root + "/credential-keys/rotate", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Vault.RotateKeyAsync(Api.Context(http), Api.Body<RotateCredentialKeyRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Rotate master key and reencrypt credentials while retaining backup keys", "Credentials"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, root + "/credential-keys/retire", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Vault.RetireKeysAsync(Api.Context(http), Api.Body<RotateCredentialKeyRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Retire earlier master keys once no credential uses them", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/endpoints/{id}/discover", http => Api.GuardAsync(http, async () =>
            {
                EndpointProfile endpoint = (await _Database.Catalog.EndpointsAsync(Api.Context(http), http.Token, Api.Parameter(http, "id")).ConfigureAwait(false)).SingleOrDefault() ?? throw new DomainException(404, "endpoint_not_found", "The endpoint does not exist.");
                await Api.SendAsync(http, await EndpointDiscoveryService.DiscoverAsync(endpoint, _Vault, Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Explicitly check connectivity and discover models without a completion", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root + "/endpoints", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Catalog.EndpointsAsync(Api.Context(http), http.Token, pageNumber: int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), pageSize: int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "100", CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List HTTP endpoints and nonsecret authentication bindings", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, root + "/endpoints", http => Api.GuardAsync(http, async () =>
            { EndpointProfile body = Api.Body<EndpointProfile>(http); body.Id = ""; await Api.SendAsync(http, await _Database.Catalog.SaveEndpointAsync(Api.Context(http), body, http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create an Ollama or OpenAI-compatible endpoint", "Configuration"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, root + "/endpoints/{id}", http => Api.GuardAsync(http, async () =>
            { EndpointProfile body = Api.Body<EndpointProfile>(http); body.Id = Api.Parameter(http, "id"); await Api.SendAsync(http, await _Database.Catalog.SaveEndpointAsync(Api.Context(http), body, http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update or disable an endpoint with its expected revision", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root + "/harnesses", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Catalog.HarnessesAsync(Api.Context(http), http.Token, pageNumber: int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), pageSize: int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "100", CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List reusable CLI harnesses", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, root + "/harnesses", http => Api.GuardAsync(http, async () =>
            { HarnessProfile body = Api.Body<HarnessProfile>(http); body.Id = ""; await Api.SendAsync(http, await _Database.Catalog.SaveHarnessAsync(Api.Context(http), body, http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create a reusable CLI harness", "Configuration"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, root + "/harnesses/{id}", http => Api.GuardAsync(http, async () =>
            { HarnessProfile body = Api.Body<HarnessProfile>(http); body.Id = Api.Parameter(http, "id"); await Api.SendAsync(http, await _Database.Catalog.SaveHarnessAsync(Api.Context(http), body, http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update or disable a CLI harness with its expected revision", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root + "/models", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Catalog.ModelsAsync(Api.Context(http), http.Token, pageNumber: int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), pageSize: int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "100", CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List reusable model capability declarations", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, root + "/models", http => Api.GuardAsync(http, async () =>
            { ModelProfile body = Api.Body<ModelProfile>(http); body.Id = ""; await Api.SendAsync(http, await _Database.Catalog.SaveModelAsync(Api.Context(http), body, http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create a model bound to an endpoint or harness", "Configuration"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, root + "/models/{id}", http => Api.GuardAsync(http, async () =>
            { ModelProfile body = Api.Body<ModelProfile>(http); body.Id = Api.Parameter(http, "id"); await Api.SendAsync(http, await _Database.Catalog.SaveModelAsync(Api.Context(http), body, http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update or disable a model with its expected revision", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root + "/credentials", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Credentials.EnumerateAsync(Api.Context(http), http.Token, pageNumber: int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture), pageSize: int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "100", CultureInfo.InvariantCulture)).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List safe credential metadata; values are never returned", "Credentials"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, root + "/credentials", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Vault.SaveAsync(Api.Context(http), "", Api.Body<CredentialRequest>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Import a write-only token or complete Codex auth.json", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, root + "/credentials/{id}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Vault.SaveAsync(Api.Context(http), Api.Parameter(http, "id"), Api.Body<CredentialRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Replace a credential with an expected revision", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, root + "/credentials/{id}", http => Api.GuardAsync(http, async () =>
            { await _Database.Credentials.DisableAsync(Api.Context(http), Api.Parameter(http, "id"), long.Parse(http.Request.RetrieveQueryValue("revision") ?? "0", CultureInfo.InvariantCulture), http.Token).ConfigureAwait(false); await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Disable a credential while retaining historical references", "Credentials"));
        }
    }
}
