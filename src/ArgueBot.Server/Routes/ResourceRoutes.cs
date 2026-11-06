namespace ArgueBot.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Tenant resource library: CRUD, lookup by name, revision history, revert, import and export.</summary>
    internal sealed class ResourceRoutes
    {
        private const string Root = "/v2.0/api/resources";
        private const string ByName = "by-name";
        private readonly DatabaseDriverBase _Database;
        internal ResourceRoutes(DatabaseDriverBase database) { _Database = database; }

        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, Root, http => Api.GuardAsync(http, async () =>
            {
                int pageNumber = int.Parse(http.Request.RetrieveQueryValue("pageNumber") ?? "1", CultureInfo.InvariantCulture);
                int pageSize = int.Parse(http.Request.RetrieveQueryValue("pageSize") ?? "50", CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.Resources.EnumerateAsync(Api.Context(http), http.Request.RetrieveQueryValue("search") ?? "", http.Request.RetrieveQueryValue("tag") ?? "", pageNumber, pageSize, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("List resources by name, with search and tag filters", "Resources"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, Root, http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Resources.CreateAsync(Api.Context(http), Api.Body<ResourceRequest>(http), token: http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create a resource", "Resources"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, Root + "/import", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Resources.ImportAsync(Api.Context(http), Api.Body<ResourceImportRequest>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Import markdown files as resources", "Resources"));
            // Names may contain slashes, so lookup by name takes the rest of the path.
            server.Routes.PostAuthentication.Dynamic.Add(HttpMethod.GET, new Regex("^" + Regex.Escape(Root + "/" + ByName + "/") + ".+$", RegexOptions.CultureInvariant), http => Api.GuardAsync(http, () => ReadByNameAsync(http)), openApiMetadata: OpenApiRouteMetadata.Create("Read a resource by name", "Resources"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Root + "/{resourceId}", http => Api.GuardAsync(http, async () =>
            {
                if (IsByName(http)) { await ReadByNameAsync(http).ConfigureAwait(false); return; }
                await Api.SendAsync(http, await _Database.Resources.ReadAsync(Api.Context(http), Api.Parameter(http, "resourceId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Read a resource", "Resources"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, Root + "/{resourceId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Resources.UpdateAsync(Api.Context(http), Api.Parameter(http, "resourceId"), Api.Body<ResourceRequest>(http), token: http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Update, rename, lock or unlock a resource with its expected revision", "Resources"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, Root + "/{resourceId}", http => Api.GuardAsync(http, async () =>
            {
                await _Database.Resources.DeleteAsync(Api.Context(http), Api.Parameter(http, "resourceId"), long.Parse(http.Request.RetrieveQueryValue("revision") ?? "0", CultureInfo.InvariantCulture), http.Token).ConfigureAwait(false);
                await Api.SendAsync(http, new Acknowledgement()).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Delete a resource and its history with its expected revision", "Resources"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Root + "/{resourceId}/revisions", http => Api.GuardAsync(http, async () =>
            {
                if (IsByName(http)) { await ReadByNameAsync(http).ConfigureAwait(false); return; }
                await Api.SendAsync(http, await _Database.Resources.RevisionsAsync(Api.Context(http), Api.Parameter(http, "resourceId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("List a resource's saved revisions, newest first", "Resources"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Root + "/{resourceId}/revisions/{revision}", http => Api.GuardAsync(http, async () =>
            {
                if (IsByName(http)) { await ReadByNameAsync(http).ConfigureAwait(false); return; }
                long revision = long.Parse(Api.Parameter(http, "revision"), CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.Resources.ReadRevisionAsync(Api.Context(http), Api.Parameter(http, "resourceId"), revision, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Read one saved revision with its body", "Resources"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, Root + "/{resourceId}/revert", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Resources.RevertAsync(Api.Context(http), Api.Parameter(http, "resourceId"), Api.Body<ResourceRevertRequest>(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Restore a saved revision as a new revision", "Resources"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, Root + "/{resourceId}/export", http => Api.GuardAsync(http, async () =>
            {
                if (IsByName(http)) { await ReadByNameAsync(http).ConfigureAwait(false); return; }
                Resource resource = await _Database.Resources.ReadAsync(Api.Context(http), Api.Parameter(http, "resourceId"), http.Token).ConfigureAwait(false);
                http.Response.StatusCode = 200; http.Response.ContentType = "text/markdown; charset=utf-8";
                http.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + resource.Name.Replace('/', '_') + ".md\"";
                await http.Response.Send(ResourceMarkdown.Format(resource), http.Token).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("Export a resource as markdown with front matter", "Resources"));
        }

        private static bool IsByName(HttpContextBase http)
        { return http.Request.Url.RawWithoutQuery.StartsWith(Root + "/" + ByName + "/", StringComparison.Ordinal); }

        private async Task ReadByNameAsync(HttpContextBase http)
        {
            string raw = http.Request.Url.RawWithoutQuery.Substring((Root + "/" + ByName + "/").Length);
            string name = Uri.UnescapeDataString(raw);
            await Api.SendAsync(http, await _Database.Resources.ReadByNameAsync(Api.Context(http), name, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
}
