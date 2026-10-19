namespace ArgueBot.Server
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
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
            if (File.Exists(Path.Combine(paths.Home, "runtime", "recovery-incomplete"))) throw new InvalidDataException("This recovery home has not completed reconciliation. Keep it stopped and review the retained snapshot and recovery operation before removing its incomplete-recovery marker.");
            bool restartRequested = false;
            using (X509Certificate2? certificate = ListenerCertificate.Load(paths, settings))
            using (RadiantHost telemetry = TelemetryHost.Start(settings.Telemetry))
            using (CancellationTokenSource wake = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (SettingsManager settingsManager = new SettingsManager(paths, settings))
            using (FileStream owner = paths.AcquireLock())
            using (DatabaseDriverBase database = settings.Database.Type == DatabaseTypeEnum.Sqlite && settings.Database.ConnectionString.Length == 0 && settings.Database.ConnectionEnvironmentVariable.Length == 0 ? new SqliteDatabaseDriver(paths) : DatabaseDriverFactory.Create(settings.Database, Path.Combine(paths.Home, "backups")))
            {
                telemetry.LoggerFactory?.AddProvider(new FileLogProvider(Path.Combine(paths.Home, "logs")));
                await database.InitializeAsync(token).ConfigureAwait(false);
                string homeId = LocalHomeIdentity.Ensure(paths);
                using (CancellationTokenRegistration ownership = database.OwnershipToken.Register(() => wake.Cancel()))
                await using (BackupManager backups = new BackupManager(database, paths))
                await using (EventMaintenance maintenance = new EventMaintenance(database, settings, telemetry.CreateLogger("ArgueBot.Retention"), wake.Token))
                {
                    await backups.InitializeAsync(token).ConfigureAwait(false);
                    SessionMethods sessions = new SessionMethods(database, new TokenProtector(paths));
                    using (PasswordExchange passwords = new PasswordExchange(sessions))
                    using (LocalEnrollment enrollment = new LocalEnrollment(paths, sessions))
                    {
                        ConversationEngine engine = new ConversationEngine(database, new RunnerRouter(settings.AllowCli), settings.MaxConcurrentRuns, telemetry.CreateLogger("ArgueBot.Execution"), streamBatchCharacters: settings.StreamBatchCharacters, streamCommitMilliseconds: settings.StreamCommitMilliseconds, contextMaxCharacters: settings.ContextMaxCharacters, contextMaxMessages: settings.ContextMaxMessages);
                        try
                        {
                            using (Webserver server = new Webserver(new WebserverSettings("127.0.0.1", settings.Port, settings.TlsEnabled), http => Api.SendAsync(http, new ApiError { Code = "not_found", Message = "Route not found." }, 404)))
                            {
                                if (certificate != null) server.Settings.Ssl.SslCertificate = certificate;
                                server.Settings.Ssl.AcceptInvalidAcertificates = false;
                                server.Settings.Telemetry.Enable = true;
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
                                    http.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type, x-token, x-tenant-guid, x-tenant-id, x-client-id, Last-Event-ID";
                                    http.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
                                    await Api.SendAsync(http, new { }, 204).ConfigureAwait(false);
                                });
                                RequestCapture capture = new RequestCapture(database, settings, paths);
                                server.Routes.PostRouting = capture.CaptureAsync;
                                server.Routes.AuthenticateRequest = http => Api.GuardAsync(http, async () =>
                                {
                                    string secret = Api.SessionToken(http);
                                    RequestContext? context = await sessions.AuthenticateAsync(secret, http.Token).ConfigureAwait(false);
                                    if (context == null) throw new DomainException(401, "unauthenticated", "Pair locally or provide a valid enrolled session.");
                                    context.AuthScheme = http.Request.Headers["Authorization"] == null ? "XToken" : "BearerToken";
                                    RequestContext? received = http.Metadata as RequestContext;
                                    context.CorrelationId = received?.CorrelationId ?? Guid.NewGuid().ToString("N"); context.ReceivedUtc = received?.ReceivedUtc ?? DateTime.UtcNow; context.StartedTimestamp = received?.StartedTimestamp ?? Stopwatch.GetTimestamp(); http.Metadata = context;
                                    SessionAuthentication.BindTenant(http.Request.Headers, context);
                                });
                                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/health", http => Api.GuardAsync(http, async () =>
                                {
                                    string challenge = http.Request.RetrieveQueryValue("challenge") ?? "";
                                    string proof = challenge.Length == 0 ? "" : LocalHomeIdentity.CreateProof(paths, settings.Port, challenge);
                                    await Api.SendAsync(http, new HealthResponse { HomeId = homeId, HomeProof = proof, Status = "ready", Schema = DatabaseSchema.CurrentVersion, Protocol = ServiceProtocol.CurrentVersion }).ConfigureAwait(false);
                                }), openApiMetadata: OpenApiRouteMetadata.Create("Local readiness", "Health"));
                                server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/auth/pair", http => Api.GuardAsync(http, async () =>
                                { await Api.SendAsync(http, new PairResponse { Token = await enrollment.PairAsync(Api.Body<PairRequest>(http).Code, http.Token).ConfigureAwait(false) }).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Exchange local single-use code", "Auth"));
                                server.Routes.PreAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/auth/login", http => Api.GuardAsync(http, async () =>
                                {
                                    if (!settings.TlsEnabled) throw new DomainException(403, "tls_required", "Password exchange requires an HTTPS listener.");
                                    if (Api.SessionToken(http).Length != 0) throw new DomainException(401, "conflicting_auth", "Password exchange cannot be combined with an existing session.");
                                    LoginRequest request = Api.Body<LoginRequest>(http);
                                    SessionAuthentication.BindTenant(http.Request.Headers, new RequestContext { TenantId = request.TenantId ?? "" });
                                    await Api.SendAsync(http, new SessionResponse { Token = await passwords.LoginAsync(request, (http.Metadata as RequestContext)?.CorrelationId ?? "", http.Token).ConfigureAwait(false) }).ConfigureAwait(false);
                                }), openApiMetadata: OpenApiRouteMetadata.Create("Exchange tenant-bound password material", "Auth"));
                                server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/auth/me", http => Api.SendAsync(http, Api.Context(http)), openApiMetadata: OpenApiRouteMetadata.Create("Current identity", "Auth"));
                                server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/auth/logout", http => Api.GuardAsync(http, async () =>
                                { string secret = Api.SessionToken(http); await sessions.RevokeAsync(secret, http.Token).ConfigureAwait(false); await Api.SendAsync(http, new RevocationResponse()).ConfigureAwait(false); }), openApiMetadata: OpenApiRouteMetadata.Create("Revoke session", "Auth"));
                                new DashboardRoutes().Register(server);
                                new HistoryRoutes(database, settings).Register(server);
                                new AuditRoutes(database).Register(server);
                                new UserRoutes(database).Register(server);
                                new RoleRoutes(database).Register(server);
                                new PermissionRoutes(database).Register(server);
                                new UserRoleAssignmentRoutes(database).Register(server);
                                new ConfigurationRoutes(database, settingsManager, restart => { restartRequested = restart; wake.Cancel(); }).Register(server);
                                new ConversationRoutes(database, engine, sessions, settings).Register(server);
                                new BackupRoutes(database, backups).Register(server);
                                ContractRoutes.Register(server);
                                server.UseOpenApi(options => options.DocumentPath = "/openapi.generated.json");
                                server.Start();
                                using (LocalServiceRegistration registration = new LocalServiceRegistration(paths, settings.Port, settings.TlsEnabled, certificate?.GetCertHashString(HashAlgorithmName.SHA256) ?? ""))
                                {
                                    telemetry.CreateLogger("ArgueBot.Service").LogInformation("Listener started {Port}", settings.Port);
                                    Console.WriteLine("ArgueBot replacement service: " + (settings.TlsEnabled ? "https" : "http") + "://127.0.0.1:" + settings.Port + " | home " + paths.Home);
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
