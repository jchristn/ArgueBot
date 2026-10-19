namespace ArgueBot.Server
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Database.Sqlite.Implementations;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using ArgueBot.Server.Routes;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal static class ArgueBotServer
    {
        internal static async Task RunAsync(ArgueBotPaths paths, ServiceSettings settings, CancellationToken token)
        {
            using (FileStream owner = paths.AcquireLock())
            using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(paths))
            {
                await database.InitializeAsync(token).ConfigureAwait(false);
                SessionMethods sessions = new SessionMethods(database);
                using (LocalEnrollment enrollment = new LocalEnrollment(paths, sessions))
                {
                    ConversationEngine engine = new ConversationEngine(database, new RunnerRouter(settings.AllowCli), settings.MaxConcurrentRuns);
                    try
                    {
                        using (Webserver server = new Webserver(new WebserverSettings("127.0.0.1", settings.Port, false), http => Api.SendAsync(http, new ApiError { Code = "not_found", Message = "Route not found." }, 404)))
                        {
                            server.Routes.Preflight = http => Api.SendAsync(http, new { }, 204);
                            server.Routes.PostRouting = http => Task.CompletedTask;
                            server.Routes.AuthenticateRequest = http => Api.GuardAsync(http, async () =>
                            {
                                string bearer = http.Request.Headers["Authorization"] ?? "";
                                string alternate = http.Request.Headers["x-token"] ?? "";
                                string secret = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearer.Substring(7) : alternate;
                                if (alternate.Length > 0 && secret != alternate) throw new DomainException(401, "conflicting_auth", "Token carriers disagree.");
                                RequestContext? context = await sessions.AuthenticateAsync(secret, http.Token).ConfigureAwait(false);
                                if (context == null) throw new DomainException(401, "unauthenticated", "Pair locally or provide a valid bearer session.");
                                string? tenant = http.Request.Headers["x-tenant-guid"];
                                if (!string.IsNullOrEmpty(tenant) && tenant != context.TenantId) throw new DomainException(403, "tenant_conflict", "Tenant hints disagree.");
                                context.CorrelationId = Guid.NewGuid().ToString("N"); http.Metadata = context;
                            });
                            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/health", http => Api.SendAsync(http, new { status = "ready", schema = 1 }), openApiMetadata: OpenApiRouteMetadata.Create("Local readiness", "Health"));
                            server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/auth/pair", http => Api.GuardAsync(http, async () =>
                            { await Api.SendAsync(http, new PairResponse { Token = await enrollment.PairAsync(Api.Body<PairRequest>(http).Code, http.Token).ConfigureAwait(false) }).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Exchange local single-use code", "Auth"));
                            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/auth/me", http => Api.SendAsync(http, Api.Context(http)), openApiMetadata: OpenApiRouteMetadata.Create("Current identity", "Auth"));
                            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/auth/logout", http => Api.GuardAsync(http, async () =>
                            { string secret = (http.Request.Headers["Authorization"] ?? "").Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase); await sessions.RevokeAsync(secret, http.Token).ConfigureAwait(false); await Api.SendAsync(http, new { revoked = true }).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Revoke session", "Auth"));
                            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/participants", http => Api.GuardAsync(http, async () =>
                            { await Api.SendAsync(http, await database.Profiles.EnumerateAsync(Api.Context(http), http.Token).ConfigureAwait(false)).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("List participant profiles", "Configuration"));
                            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/participants", http => Api.GuardAsync(http, async () =>
                            { await Api.SendAsync(http, await database.Profiles.CreateAsync(Api.Context(http), Api.Body<RunnerProfile>(http), http.Token).ConfigureAwait(false), 201).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Create runner profile", "Configuration"));
                            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/settings", http => Api.SendAsync(http, settings), openApiMetadata: OpenApiRouteMetadata.Create("Nonsecret bootstrap settings", "Settings"));
                            new ConversationRoutes(database, engine).Register(server);
                            server.UseOpenApi();
                            server.Start(token);
                            Console.WriteLine("ArgueBot replacement service: http://127.0.0.1:" + settings.Port + " | home " + paths.Home);
                            try { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                            server.Stop();
                        }
                    }
                    finally { await engine.DisposeAsync().ConfigureAwait(false); }
                }
            }
        }
    }
}
