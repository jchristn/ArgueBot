namespace ArgueBot.Server.Routes
{
    using System.Globalization;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class RoleRoutes
    {
        private readonly DatabaseDriverBase _Database;
        internal RoleRoutes(DatabaseDriverBase database) { _Database = database; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/roles", http => Api.GuardAsync(http, async () =>
            {
                int page = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture);
                int size = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "25", CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.Roles.EnumerateAsync(Api.Context(http), http.Request.RetrieveQueryValue("search") ?? "", page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Enumerate visible role definitions", "Roles"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/roles", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Roles.CreateAsync(Api.Context(http), Api.Body<RoleRequest>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create tenant custom role", "Roles"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/roles/{roleId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Roles.ReadAsync(Api.Context(http), Api.Parameter(http, "roleId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read visible role definition", "Roles"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/roles/{roleId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Roles.UpdateAsync(Api.Context(http), Api.Parameter(http, "roleId"), Api.Body<RoleRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update custom role under optimistic concurrency", "Roles"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/roles/{roleId}/permissions", http => Api.GuardAsync(http, async () =>
            {
                int page = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture);
                int size = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "25", CultureInfo.InvariantCulture);
                bool active = bool.Parse(http.Request.RetrieveQueryValue("active") ?? "true");
                await Api.SendAsync(http, await _Database.RolePermissions.EnumerateAsync(Api.Context(http), Api.Parameter(http, "roleId"), active, page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Enumerate role permission mappings", "Roles"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/roles/{roleId}/permissions", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.RolePermissions.ReplaceAsync(Api.Context(http), Api.Parameter(http, "roleId"), Api.Body<RolePermissionsRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Replace active custom role permissions", "Roles"));
        }
    }
}
