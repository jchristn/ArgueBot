namespace ArgueBot.Server.Routes
{
    using System.Globalization;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class UserRoleAssignmentRoutes
    {
        private readonly DatabaseDriverBase _Database;
        internal UserRoleAssignmentRoutes(DatabaseDriverBase database) { _Database = database; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/users/{userId}/role-assignments", http => Api.GuardAsync(http, async () =>
            {
                int page = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture);
                int size = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "25", CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.UserRoleAssignments.EnumerateAsync(Api.Context(http), Api.Parameter(http, "userId"), page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Enumerate scoped user role assignments", "Assignments"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/users/{userId}/role-assignments", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.UserRoleAssignments.CreateAsync(Api.Context(http), Api.Parameter(http, "userId"), Api.Body<UserRoleAssignmentRequest>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create scoped user role assignment", "Assignments"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/users/{userId}/role-assignments/{assignmentId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.UserRoleAssignments.ReadAsync(Api.Context(http), Api.Parameter(http, "userId"), Api.Parameter(http, "assignmentId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read scoped user role assignment", "Assignments"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/users/{userId}/role-assignments/{assignmentId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.UserRoleAssignments.UpdateAsync(Api.Context(http), Api.Parameter(http, "userId"), Api.Parameter(http, "assignmentId"), Api.Body<UserRoleAssignmentRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update scoped user role assignment", "Assignments"));
        }
    }
}
