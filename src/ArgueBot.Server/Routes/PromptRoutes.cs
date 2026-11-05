namespace ArgueBot.Server.Routes
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System.Globalization;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Debate prompt library and its placeholder legend.</summary>
    internal sealed class PromptRoutes
    {
        private readonly DatabaseDriverBase _Database;
        internal PromptRoutes(DatabaseDriverBase database) { _Database = database; }
        internal void Register(Webserver server)
        {
            const string root = "/v2.0/api/prompts";
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v2.0/api/prompt-parameters", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, PromptCatalog.Parameters).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List prompt placeholders and the debate steps where each has a value", "Prompts"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root, http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Prompts.EnumerateAsync(Api.Context(http), http.Request.RetrieveQueryValue("purpose") ?? "", http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List factory and workspace prompts", "Prompts"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, root, http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Prompts.CreateAsync(Api.Context(http), Api.Body<PromptTemplate>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create a workspace prompt", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/{id}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Prompts.ReadAsync(Api.Context(http), Api.Parameter(http, "id"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read a prompt", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, root + "/{id}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Prompts.UpdateAsync(Api.Context(http), Api.Parameter(http, "id"), Api.Body<PromptTemplate>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update a workspace prompt with its expected revision", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, root + "/{id}/duplicate", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Prompts.DuplicateAsync(Api.Context(http), Api.Parameter(http, "id"), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Copy a prompt, including a factory default, into an editable workspace prompt", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, root + "/{id}", http => Api.GuardAsync(http, async () =>
            {
                await _Database.Prompts.DeleteAsync(Api.Context(http), Api.Parameter(http, "id"), long.Parse(http.Request.RetrieveQueryValue("revision") ?? "0", CultureInfo.InvariantCulture), http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Delete a workspace prompt; factory prompts cannot be deleted", "Prompts"));
        }
    }
}
