namespace ArgueBot.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class HistoryRoutes
    {
        private readonly DatabaseDriverBase _Database;
        private readonly ServiceSettings _Settings;
        internal HistoryRoutes(DatabaseDriverBase database, ServiceSettings settings) { _Database = database; _Settings = settings; }
        internal void Register(Webserver server)
        {
            const string root = "/v1.0/api/request-history";
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root, http => Api.GuardAsync(http, async () =>
            {
                HistoryFilter filter = new HistoryFilter { Search = http.Request.RetrieveQueryValue("search") ?? "", Method = http.Request.RetrieveQueryValue("method") ?? "", Status = int.TryParse(http.Request.RetrieveQueryValue("status"), out int status) ? status : null, PageNumber = int.TryParse(http.Request.RetrieveQueryValue("pageNumber"), out int page) ? page : 1, PageSize = int.TryParse(http.Request.RetrieveQueryValue("pageSize"), out int size) ? size : 25, FromUtc = DateTime.TryParse(http.Request.RetrieveQueryValue("fromUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime from) ? from.ToUniversalTime() : null, ToUtc = DateTime.TryParse(http.Request.RetrieveQueryValue("toUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime to) ? to.ToUniversalTime() : null };
                await Api.SendAsync(http, _Settings.RequestHistoryEnabled ? await _Database.History.EnumerateAsync(Api.Context(http), filter, http.Token).ConfigureAwait(false) : new HistoryPage { PageNumber = filter.PageNumber, PageSize = filter.PageSize }).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Filter and page retained request metadata", "Request History"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, root + "/summary", http => Api.GuardAsync(http, async () =>
            {
                DateTime from = DateTime.Parse(http.Request.RetrieveQueryValue("fromUtc") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                DateTime to = DateTime.Parse(http.Request.RetrieveQueryValue("toUtc") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                int minutes = int.TryParse(http.Request.RetrieveQueryValue("bucketMinutes"), out int size) ? size : 60;
                await Api.SendAsync(http, _Settings.RequestHistoryEnabled ? await _Database.History.SummaryAsync(Api.Context(http), from, to, minutes, http.Token).ConfigureAwait(false) : new List<ActivityBucket>()).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Time bucket summary with zero-filled gaps", "Request History"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, root + "/{requestId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.History.ReadAsync(Api.Context(http), Api.Parameter(http, "requestId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Inspect redacted headers and body prefixes", "Request History"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, root + "/{requestId}", http => Api.GuardAsync(http, async () =>
            { await _Database.History.DeleteAsync(Api.Context(http), Api.Parameter(http, "requestId"), http.Token).ConfigureAwait(false); await Api.SendAsync(http, new DeletionResponse()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Delete retained request capture", "Request History"));
        }
    }
}
