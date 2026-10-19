namespace ArgueBot.Server.Routes
{
    using System.Globalization;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class UserRoutes
    {
        private readonly DatabaseDriverBase _Database;
        internal UserRoutes(DatabaseDriverBase database) { _Database = database; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/users", http => Api.GuardAsync(http, async () =>
            {
                int page = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture);
                int size = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "25", CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.Users.EnumerateAsync(Api.Context(http), http.Request.RetrieveQueryValue("search") ?? "", page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Enumerate tenant identities", "Users"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/users", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Users.CreateAsync(Api.Context(http), Api.Body<UserRequest>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create tenant identity", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/users/{userId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Users.ReadAsync(Api.Context(http), Api.Parameter(http, "userId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read tenant identity metadata", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/users/{userId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Users.UpdateAsync(Api.Context(http), Api.Parameter(http, "userId"), Api.Body<UserRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update tenant identity and revoke affected sessions", "Users"));
        }
    }
}
