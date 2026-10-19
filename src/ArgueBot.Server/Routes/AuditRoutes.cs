namespace ArgueBot.Server.Routes
{
    using System.Globalization;
    using ArgueBot.Core.Database;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class AuditRoutes
    {
        private readonly DatabaseDriverBase _Database;
        internal AuditRoutes(DatabaseDriverBase database) { _Database = database; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/audit", http => Api.GuardAsync(http, async () =>
            {
                int page = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture);
                int size = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "25", CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.Audit.EnumerateAsync(Api.Context(http), http.Request.RetrieveQueryValue("operation") ?? "", page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Inspect append-only scoped security accounting", "Audit"));
        }
    }
}
