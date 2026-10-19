namespace ArgueBot.Server
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using ArgueBot.Server.Routes;
    using ArgueBot.Server.Services;
    using Radiant;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class ArgueBotServer
    {
        internal async Task<bool> RunAsync(ArgueBotPaths paths, ServiceSettings settings, CancellationToken token)
        {
            bool restartRequested = false;
            using (RadiantHost telemetry = TelemetryHost.Start(settings.Telemetry))
            using (CancellationTokenSource wake = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (SettingsManager settingsManager = new SettingsManager(paths, settings))
            using (FileStream owner = paths.AcquireLock())
            using (DatabaseDriverBase database = settings.Database.Type == DatabaseTypeEnum.Sqlite && settings.Database.ConnectionString.Length == 0 && settings.Database.ConnectionEnvironmentVariable.Length == 0 ? new SqliteDatabaseDriver(paths) : DatabaseDriverFactory.Create(settings.Database, Path.Combine(paths.Home, "backups")))
            {
                telemetry.LoggerFactory?.AddProvider(new FileLogProvider(Path.Combine(paths.Home, "logs")));
                await database.InitializeAsync(token).ConfigureAwait(false);
                using (CancellationTokenRegistration ownership = database.OwnershipToken.Register(() => wake.Cancel()))
                await using (BackupManager backups = new BackupManager(database, paths))
                {
                    await backups.InitializeAsync(token).ConfigureAwait(false);
                    SessionMethods sessions = new SessionMethods(database, new TokenProtector(paths));
                    using (LocalEnrollment enrollment = new LocalEnrollment(paths, sessions))
                    {
                        ConversationEngine engine = new ConversationEngine(database, new RunnerRouter(settings.AllowCli), settings.MaxConcurrentRuns, telemetry.CreateLogger("ArgueBot.Execution"));
                        try
                        {
                            using (Webserver server = new Webserver(new WebserverSettings("127.0.0.1", settings.Port, false), http => Api.SendAsync(http, new ApiError { Code = "not_found", Message = "Route not found." }, 404)))
                            {
                                server.Settings.Telemetry.Enable = true;
                                server.Settings.Headers.DefaultHeaders.Clear();
                                server.Settings.Headers.DefaultHeaders["Cache-Control"] = "no-store";
                                server.Settings.Headers.DefaultHeaders["X-Content-Type-Options"] = "nosniff";
                                server.Settings.IO.MaxRequestBodySize = 1048576;
                                server.Routes.PreRouting = http => Api.GuardAsync(http, async () =>
                                {
                                    http.Metadata = new RequestContext { CorrelationId = Guid.NewGuid().ToString("N"), ReceivedUtc = DateTime.UtcNow, StartedTimestamp = Stopwatch.GetTimestamp() };
                                    await LocalHttpPolicy.ValidateAsync(http, settings.Port).ConfigureAwait(false);
                                });
                                server.Routes.Preflight = http => Api.GuardAsync(http, async () =>
                                {
                                    await LocalHttpPolicy.ValidateAsync(http, settings.Port).ConfigureAwait(false);
                                    http.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type, x-client-id, Last-Event-ID";
                                    http.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
                                    await Api.SendAsync(http, new { }, 204).ConfigureAwait(false);
                                });
                                RequestCapture capture = new RequestCapture(database, settings, paths);
                                server.Routes.PostRouting = capture.CaptureAsync;
                                server.Routes.AuthenticateRequest = http => Api.GuardAsync(http, async () =>
                                {
                                    string bearer = http.Request.Headers["Authorization"] ?? "";
                                    string alternate = http.Request.Headers["x-token"] ?? "";
                                    if (bearer.Length > 0 && !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) throw new DomainException(401, "unsupported_auth", "Use a bearer session for this local service.");
                                    string secret = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearer.Substring(7) : alternate;
                                    if (alternate.Length > 0 && secret != alternate) throw new DomainException(401, "conflicting_auth", "Token carriers disagree.");
                                    RequestContext? context = await sessions.AuthenticateAsync(secret, http.Token).ConfigureAwait(false);
                                    if (context == null) throw new DomainException(401, "unauthenticated", "Pair locally or provide a valid bearer session.");
                                    string? tenant = http.Request.Headers["x-tenant-guid"];
                                    if (!string.IsNullOrEmpty(tenant) && tenant != context.TenantId) throw new DomainException(403, "tenant_conflict", "Tenant hints disagree.");
                                    RequestContext? received = http.Metadata as RequestContext;
                                    context.CorrelationId = received?.CorrelationId ?? Guid.NewGuid().ToString("N"); context.ReceivedUtc = received?.ReceivedUtc ?? DateTime.UtcNow; context.StartedTimestamp = received?.StartedTimestamp ?? Stopwatch.GetTimestamp(); http.Metadata = context;
                                });
                                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/health", http => Api.SendAsync(http, new HealthResponse { Status = "ready", Schema = DatabaseSchema.CurrentVersion, Protocol = 1 }), openApiMetadata: OpenApiRouteMetadata.Create("Local readiness", "Health"));
                                server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/auth/pair", http => Api.GuardAsync(http, async () =>
                                { await Api.SendAsync(http, new PairResponse { Token = await enrollment.PairAsync(Api.Body<PairRequest>(http).Code, http.Token).ConfigureAwait(false) }).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Exchange local single-use code", "Auth"));
                                server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/auth/me", http => Api.SendAsync(http, Api.Context(http)), openApiMetadata: OpenApiRouteMetadata.Create("Current identity", "Auth"));
                                server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/auth/logout", http => Api.GuardAsync(http, async () =>
                                { string secret = Api.SessionToken(http); await sessions.RevokeAsync(secret, http.Token).ConfigureAwait(false); await Api.SendAsync(http, new RevocationResponse()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Revoke session", "Auth"));
                                new DashboardRoutes().Register(server);
                                new HistoryRoutes(database, settings).Register(server);
                                new ConfigurationRoutes(database, settingsManager, restart => { restartRequested = restart; wake.Cancel(); }).Register(server);
                                new ConversationRoutes(database, engine, sessions).Register(server);
                                new BackupRoutes(database, backups).Register(server);
                                ContractRoutes.Register(server);
                                server.UseOpenApi(options => options.DocumentPath = "/openapi.generated.json");
                                server.Start();
                                using (LocalServiceRegistration registration = new LocalServiceRegistration(paths, settings.Port))
                                {
                                    telemetry.CreateLogger("ArgueBot.Service").LogInformation("Listener started {Port}", settings.Port);
                                    Console.WriteLine("ArgueBot replacement service: http://127.0.0.1:" + settings.Port + " | home " + paths.Home);
                                    try { await Task.Delay(Timeout.Infinite, wake.Token).ConfigureAwait(false); }
                                    catch (OperationCanceledException) when (wake.IsCancellationRequested) { }
                                    if (database.OwnershipToken.IsCancellationRequested) telemetry.CreateLogger("ArgueBot.Service").LogError("Exclusive database ownership was lost; stopping listener and execution workers.");
                                    server.Stop();
                                    telemetry.CreateLogger("ArgueBot.Service").LogInformation("Listener stopped {Restart}", restartRequested);
                                }
                            }
                        }
                        finally { await engine.DisposeAsync().ConfigureAwait(false); }
                        if (database.OwnershipToken.IsCancellationRequested) throw new IOException("Exclusive database ownership was lost. The service stopped without reconnecting its writer.");
                    }
                }
            }
            return restartRequested;
        }
    }
}
