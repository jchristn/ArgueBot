namespace ArgueBot.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class ConfigurationRoutes
    {
        private readonly DatabaseDriverBase _Database;
        private readonly SettingsManager _Settings;
        private readonly Action _Restart;
        internal ConfigurationRoutes(DatabaseDriverBase database, SettingsManager settings, Action restart) { _Database = database; _Settings = settings; _Restart = restart; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/participants", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Profiles.EnumerateAsync(Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List participant profiles", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/participants", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Profiles.CreateAsync(Api.Context(http), Api.Body<RunnerProfile>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create participant profile", "Configuration"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/participants/{profileId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Profiles.UpdateAsync(Api.Context(http), Api.Parameter(http, "profileId"), Api.Body<RunnerProfile>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Edit profile without changing historical snapshots", "Configuration"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/participants/{profileId}", http => Api.GuardAsync(http, async () =>
            { await _Database.Profiles.DeleteAsync(Api.Context(http), Api.Parameter(http, "profileId"), http.Token).ConfigureAwait(false); await Api.SendAsync(http, new DeletionResponse()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Delete reusable profile; keep history", "Configuration"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/settings", http => Api.GuardAsync(http, async () =>
            { Admin(http); await Api.SendAsync(http, await _Settings.ReadAsync(http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read redacted configured settings and application metadata", "Settings"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/v1.0/api/settings", http => Api.GuardAsync(http, async () =>
            { Admin(http); await Api.SendAsync(http, await _Settings.SaveAsync(Api.Body<SettingsUpdate>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Save validated settings using expected file revision", "Settings"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/settings/restart", http => Api.GuardAsync(http, async () =>
            { Admin(http); await Api.SendAsync(http, new RestartResponse(), 202).ConfigureAwait(false); _Restart(); }), openApiMetadata: OpenApiRouteMetadata.Create("Drain and restart the local service", "Settings"));
        }
        private static void Admin(HttpContextBase http) { if (!Api.Context(http).IsAdmin) throw new DomainException(403, "forbidden", "Service administration requires an administrator."); }
    }
}
