namespace ArgueBot.Server
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using ArgueBot.Server.Routes;
    using ArgueBot.Server.Services;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    internal sealed class ArgueBotServer
    {
        internal async Task<bool> RunAsync(ArgueBotPaths paths, ServiceSettings settings, CancellationToken token)
        {
            if (File.Exists(Path.Combine(paths.Home, "runtime", "recovery-incomplete"))) throw new InvalidDataException("This recovery home has not completed reconciliation. Keep it stopped and review the retained snapshot and recovery operation before removing its incomplete-recovery marker.");
            bool restartRequested = false;
            using (X509Certificate2? certificate = ListenerCertificate.Load(paths, settings))
            using (FileLogProvider logger = new FileLogProvider(Path.Combine(paths.Home, "logs")))
            using (CancellationTokenSource wake = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (SettingsManager settingsManager = new SettingsManager(paths, settings))
            using (FileStream owner = paths.AcquireLock())
            using (DatabaseDriverBase database = settings.Database.Type == DatabaseTypeEnum.Sqlite && settings.Database.ConnectionString.Length == 0 && settings.Database.ConnectionEnvironmentVariable.Length == 0 ? new SqliteDatabaseDriver(paths) : DatabaseDriverFactory.Create(settings.Database, Path.Combine(paths.Home, "backups")))
            {
                await database.InitializeAsync(token).ConfigureAwait(false);
                string homeId = LocalHomeIdentity.Ensure(paths);
                using (CancellationTokenRegistration ownership = database.OwnershipToken.Register(() => wake.Cancel()))
                await using (EventMaintenance maintenance = new EventMaintenance(database, settings, logger.CreateLogger("ArgueBot.Retention"), wake.Token))
                {
                    {
                        File.Delete(Path.Combine(paths.Home, "runtime", "pairing-code"));
                        File.Delete(Path.Combine(paths.Home, "runtime", "pairing-code.tmp"));
                        File.Delete(Path.Combine(paths.Home, "runtime", "cli-session.json"));
                        ConversationEngine engine = new ConversationEngine(database, new RunnerRouter(settings.AllowCli), settings.MaxConcurrentRuns, logger.CreateLogger("ArgueBot.Execution"), streamBatchCharacters: settings.StreamBatchCharacters, streamCommitMilliseconds: settings.StreamCommitMilliseconds, contextMaxCharacters: settings.ContextMaxCharacters, contextMaxMessages: settings.ContextMaxMessages, diagnosticDirectory: Path.Combine(paths.Home, "logs", "runner-failures"));
                        try
                        {
                            using (Webserver server = new Webserver(new WebserverSettings("127.0.0.1", settings.Port, settings.TlsEnabled), http => Api.SendAsync(http, new ApiError { Code = "not_found", Message = "Route not found." }, 404)))
                            {
                                if (certificate != null) server.Settings.Ssl.SslCertificate = certificate;
                                server.Settings.Ssl.AcceptInvalidAcertificates = false;
                                server.Settings.Telemetry.Enable = false;
                                server.Settings.Headers.DefaultHeaders.Clear();
                                server.Settings.Headers.DefaultHeaders["Cache-Control"] = "no-store";
                                server.Settings.Headers.DefaultHeaders["X-Content-Type-Options"] = "nosniff";
                                server.Settings.IO.MaxRequestBodySize = 1048576;
                                server.Routes.PreRouting = http => Api.GuardAsync(http, async () =>
                                {
                                    http.Metadata = new RequestContext { CorrelationId = Guid.NewGuid().ToString("N"), ReceivedUtc = DateTime.UtcNow, StartedTimestamp = Stopwatch.GetTimestamp() };
                                    await LocalHttpPolicy.ValidateAsync(http, settings.Port, settings.TlsEnabled).ConfigureAwait(false);
                                });
                                server.Routes.Preflight = http => Api.GuardAsync(http, async () =>
                                {
                                    await LocalHttpPolicy.ValidateAsync(http, settings.Port, settings.TlsEnabled).ConfigureAwait(false);
                                    http.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, x-client-id, Last-Event-ID";
                                    http.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
                                    await Api.SendAsync(http, new { }, 204).ConfigureAwait(false);
                                });
                                RequestCapture capture = new RequestCapture(database, settings, paths);
                                server.Routes.PostRouting = capture.CaptureAsync;
                                server.Routes.AuthenticateRequest = http => Api.GuardAsync(http, () =>
                                {
                                    RequestContext? received = http.Metadata as RequestContext;
                                    http.Metadata = new RequestContext { TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, CorrelationId = received?.CorrelationId ?? Guid.NewGuid().ToString("N"), ReceivedUtc = received?.ReceivedUtc ?? DateTime.UtcNow, StartedTimestamp = received?.StartedTimestamp ?? Stopwatch.GetTimestamp() };
                                    return Task.CompletedTask;
                                });
                                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/health", http => Api.GuardAsync(http, async () =>
                                {
                                    string challenge = http.Request.RetrieveQueryValue("challenge") ?? "";
                                    string proof = challenge.Length == 0 ? "" : LocalHomeIdentity.CreateProof(paths, settings.Port, challenge);
                                    await Api.SendAsync(http, new HealthResponse { HomeId = homeId, HomeProof = proof, Status = "ready", AuthenticationRequired = false, Schema = DatabaseSchema.CurrentVersion, Protocol = ServiceProtocol.CurrentVersion }).ConfigureAwait(false);
                                }), openApiMetadata: OpenApiRouteMetadata.Create("Local readiness", "Health"));
                                new DashboardRoutes().Register(server);
                                new HistoryRoutes(database, settings).Register(server);
                                new ConfigurationRoutes(database, settingsManager, restart => { restartRequested = restart; wake.Cancel(); }).Register(server);
                                new ConversationRoutes(database, engine, settings).Register(server);
                                ContractRoutes.Register(server);
                                server.UseOpenApi(options => options.DocumentPath = "/openapi.generated.json");
                                server.Start();
                                using (LocalServiceRegistration registration = new LocalServiceRegistration(paths, settings.Port, settings.TlsEnabled, certificate?.GetCertHashString(HashAlgorithmName.SHA256) ?? ""))
                                {
                                    logger.CreateLogger("ArgueBot.Service").LogInformation("Listener started {Port}", settings.Port);
                                    Console.WriteLine("ArgueBot replacement service: " + (settings.TlsEnabled ? "https" : "http") + "://127.0.0.1:" + settings.Port + " | home " + paths.Home);
                                    Console.WriteLine("Open the dashboard in your browser: " + (settings.TlsEnabled ? "https" : "http") + "://127.0.0.1:" + settings.Port + "/dashboard");
                                    try { await Task.Delay(Timeout.Infinite, wake.Token).ConfigureAwait(false); }
                                    catch (OperationCanceledException) when (wake.IsCancellationRequested) { }
                                    if (database.OwnershipToken.IsCancellationRequested) logger.CreateLogger("ArgueBot.Service").LogError("Exclusive database ownership was lost; stopping listener and execution workers.");
                                    server.Stop();
                                    logger.CreateLogger("ArgueBot.Service").LogInformation("Listener stopped {Restart}", restartRequested);
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
