namespace ArgueBot.Server.Routes
{
    using System.Globalization;
    using System.IO;
    using System;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Server.Services;
    using WatsonWebserver.Core.OpenApi;
    using WatsonWebserver.Core;
    using WatsonWebserver;

    internal sealed class BackupRoutes
    {
        private readonly DatabaseDriverBase _Database;
        private readonly BackupManager _Manager;
        internal BackupRoutes(DatabaseDriverBase database, BackupManager manager) { _Database = database; _Manager = manager; }
        internal void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/backups", http => Api.GuardAsync(http, async () =>
            {
                int page = int.Parse(http.Request.Query.Elements["pageNumber"] ?? "1", CultureInfo.InvariantCulture);
                int size = int.Parse(http.Request.Query.Elements["pageSize"] ?? "25", CultureInfo.InvariantCulture);
                await Api.SendAsync(http, await _Database.Backups.EnumerateAsync(Api.Context(http), http.Request.Query.Elements["search"] ?? "", page, size, http.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }), openApiMetadata: OpenApiRouteMetadata.Create("List global recovery artifacts", "Backups"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/backups", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Manager.AcceptAsync(Api.Context(http), "create", null, Api.Body<BackupRequest>(http), http.Token).ConfigureAwait(false), 202).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Queue a complete recovery archive", "Backups"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/backups/{backupId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Backups.ReadAsync(Api.Context(http), Api.Parameter(http, "backupId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read recovery artifact metadata", "Backups"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/backup-operations/{operationId}", http => Api.GuardAsync(http, async () =>
            { await Api.SendAsync(http, await _Database.Backups.ReadOperationAsync(Api.Context(http), Api.Parameter(http, "operationId"), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Read durable recovery operation status", "Backups"));
            foreach (string kind in new[] { "verify", "restore" })
            {
                string action = kind;
                server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/backups/{backupId}/" + action, http => Api.GuardAsync(http, async () =>
                { await Api.SendAsync(http, await _Manager.AcceptAsync(Api.Context(http), action, Api.Parameter(http, "backupId"), Api.Body<BackupRequest>(http), http.Token).ConfigureAwait(false), 202).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Queue archive " + action, "Backups"));
            }
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/backups/{backupId}/download", http => Api.GuardAsync(http, async () =>
            {
                BackupEntry entry = await _Database.Backups.ReadAsync(Api.Context(http), Api.Parameter(http, "backupId"), http.Token).ConfigureAwait(false);
                string path = _Manager.ArtifactPath(entry.Id);
                if (entry.Status != "ready" || !File.Exists(path)) throw new DomainException(409, "backup_unavailable", "This recovery archive is unavailable.");
                http.Response.ContentType = "application/octet-stream";
                http.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + entry.Id + ".arguebak\"";
                using (FileStream stream = File.OpenRead(path)) { await http.Response.Send(stream.Length, stream, http.Token).ConfigureAwait(false); }
            }), openApiMetadata: OpenApiRouteMetadata.Create("Download a ready recovery archive", "Backups"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/backups/{backupId}", http => Api.GuardAsync(http, async () =>
            { await _Manager.DeleteAsync(Api.Context(http), Api.Parameter(http, "backupId"), http.Token).ConfigureAwait(false); await Api.SendAsync(http, new DeletionResponse()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Delete an idle recovery artifact", "Backups"));
        }
    }
}
