namespace ArgueBot.Server.Routes
{
    using System.Globalization;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class PermissionRoutes
    {
        private readonly DatabaseDriverBase _Database;
        internal PermissionRoutes(DatabaseDriverBase database) { _Database = database; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/permissions", http => Api.GuardAsync(http, async () =>
            {
                int page = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture);
                int size = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "25", CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.Permissions.EnumerateAsync(Api.Context(http), http.Request.RetrieveQueryValue("search") ?? "", page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Enumerate visible permission definitions", "Permissions"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/permissions", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Permissions.CreateAsync(Api.Context(http), Api.Body<PermissionRequest>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create tenant custom permission", "Permissions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/permissions/{permissionId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Permissions.ReadAsync(Api.Context(http), Api.Parameter(http, "permissionId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read visible permission definition", "Permissions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/permissions/{permissionId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Permissions.UpdateAsync(Api.Context(http), Api.Parameter(http, "permissionId"), Api.Body<PermissionRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update custom permission under optimistic concurrency", "Permissions"));
        }
    }
}
