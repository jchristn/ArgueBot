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
    using System.Collections.Generic;
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
        private static List<string> BridgeCommand()
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The service executable is unavailable for its MCP bridge.");
            string assembly = Environment.GetCommandLineArgs()[0];
            return Path.GetExtension(assembly).Equals(".dll", StringComparison.OrdinalIgnoreCase) ? new List<string> { executable, Path.GetFullPath(assembly) } : new List<string> { executable };
        }
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
                // Service-owned work (key management, startup reconciliation) runs as the default administrator in the default tenant.
                // Runs and model checks carry their own tenant into the runner; see RunnerProfile.RuntimeTenantId.
                RequestContext systemContext = new RequestContext { TenantId = IdentitySeed.TenantId, UserId = IdentitySeed.UserId, IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, Role = "admin", PrincipalType = "Service" };
                SessionProtector sessions = SessionProtector.Open(Path.Combine(paths.Home, "secrets"));
                database.Sealer = TokenSealer.Open(Path.Combine(paths.Home, "secrets"));
                RequestAuthenticator authenticator = new RequestAuthenticator(database, sessions);
                LocalHttpPolicy hostPolicy = new LocalHttpPolicy(settings.Port, settings.TlsEnabled, settings.ListenAddress == "0.0.0.0", settings.AllowedHosts);
                using (CredentialVault vault = await CredentialVault.OpenAsync(database, Path.Combine(paths.Home, "secrets"), systemContext, token).ConfigureAwait(false))
                {
                database.Debates.MaximumParticipants = settings.MaxParticipants;
                string homeId = LocalHomeIdentity.Ensure(paths);
                using (CancellationTokenRegistration ownership = database.OwnershipToken.Register(() => wake.Cancel()))
                await using (EventMaintenance maintenance = new EventMaintenance(database, settings, logger.CreateLogger("ArgueBot.Retention"), wake.Token))
                {
                    {
                        File.Delete(Path.Combine(paths.Home, "runtime", "pairing-code"));
                        File.Delete(Path.Combine(paths.Home, "runtime", "pairing-code.tmp"));
                        File.Delete(Path.Combine(paths.Home, "runtime", "cli-session.json"));
                        CredentialRunner runner = new CredentialRunner(settings.AllowCli, vault, database, systemContext, Path.Combine(paths.Home, "runtime", "credential-homes"), BridgeCommand());
                        await database.ModelChecks.ReconcileAsync(token).ConfigureAwait(false);
                        await using (ModelCheckService checks = new ModelCheckService(database, runner))
                        {
                        ConversationEngine engine = new ConversationEngine(database, runner, settings.MaxConcurrentRuns, logger.CreateLogger("ArgueBot.Execution"), streamBatchCharacters: settings.StreamBatchCharacters, streamCommitMilliseconds: settings.StreamCommitMilliseconds, contextMaxCharacters: settings.ContextMaxCharacters, contextMaxMessages: settings.ContextMaxMessages, diagnosticDirectory: Path.Combine(paths.Home, "logs", "runner-failures"));
                        engine.MaxLiveQuestionHolds = settings.MaxLiveQuestionHolds;
                        try
                        {
                            using (Webserver server = new Webserver(new WebserverSettings(settings.ListenAddress, settings.Port, settings.TlsEnabled), http => Api.SendAsync(http, new ApiError { Code = "not_found", Message = "Route not found." }, 404)))
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
                                    http.Metadata = new RequestContext { CorrelationId = Guid.NewGuid().ToString("N"), ReceivedUtc = DateTime.UtcNow, StartedTimestamp = Stopwatch.GetTimestamp(), SourceIp = http.Request.Source.IpAddress ?? "" };
                                    await hostPolicy.ValidateAsync(http).ConfigureAwait(false);
                                });
                                server.Routes.Preflight = http => Api.GuardAsync(http, async () =>
                                {
                                    await hostPolicy.ValidateAsync(http).ConfigureAwait(false);
                                    http.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, x-token, x-tenant-id, x-email, x-password, x-client-id, Last-Event-ID";
                                    http.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
                                    await Api.SendAsync(http, new { }, 204).ConfigureAwait(false);
                                });
                                RequestCapture capture = new RequestCapture(database, settings, paths);
                                server.Routes.PostRouting = capture.CaptureAsync;
                                server.Routes.AuthenticateRequest = async http =>
                                {
                                    await Api.GuardAsync(http, () => RequestGate.AuthenticateAsync(http, authenticator)).ConfigureAwait(false);
                                    // Fail closed: if authentication neither signed the request in nor answered it (for example the client
                                    // disconnected mid-check), terminate it so no route handler runs without a signed-in context.
                                    if (!http.Response.ResponseSent && !(http.Metadata is RequestContext { IsAuthenticated: true }))
                                        throw new OperationCanceledException("Authentication did not complete.");
                                };
                                server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/health", http => Api.GuardAsync(http, async () =>
                                {
                                    string challenge = http.Request.RetrieveQueryValue("challenge") ?? "";
                                    string proof = challenge.Length == 0 ? "" : LocalHomeIdentity.CreateProof(paths, settings.Port, challenge);
                                    await Api.SendAsync(http, new HealthResponse { HomeId = homeId, HomeProof = proof, Status = "ready", AuthenticationRequired = true, Schema = DatabaseSchema.CurrentVersion, Protocol = ServiceProtocol.CurrentVersion }).ConfigureAwait(false);
                                }), openApiMetadata: OpenApiRouteMetadata.Create("Local readiness", "Health"));
                                new DashboardRoutes().Register(server);
                                new TokenRoutes(database, authenticator).Register(server);
                                new IdentityRoutes(database, authenticator).Register(server);
                                new ShareRoutes(database, settings.TlsEnabled).Register(server);
                                new HistoryRoutes(database, settings).Register(server);
                                new ConfigurationRoutes(database, settingsManager, restart => { restartRequested = restart; wake.Cancel(); }).Register(server);
                                new ConversationRoutes(database, engine, settings).Register(server);
                                new HistoryRoutes(database, settings).Register(server, "/v2.0/api");
                                new ConfigurationRoutes(database, settingsManager, restart => { restartRequested = restart; wake.Cancel(); }).Register(server, "/v2.0/api");
                                new ConversationRoutes(database, engine, settings).Register(server, "/v2.0/api");
                                new DebateRoutes(database, engine).Register(server);
                                new CatalogRoutes(database, vault, checks).Register(server);
                                new PromptRoutes(database).Register(server);
                                new ResourceRoutes(database).Register(server);
                                new QuestionRoutes(database, engine).Register(server);
                                ContractRoutes.Register(server);
                                server.UseOpenApi(options => options.DocumentPath = "/openapi.generated.json");
                                server.Start();
                                using (LocalServiceRegistration registration = new LocalServiceRegistration(paths, settings.Port, settings.TlsEnabled, certificate?.GetCertHashString(HashAlgorithmName.SHA256) ?? ""))
                                {
                                    logger.CreateLogger("ArgueBot.Service").LogInformation("Listener started {Port}", settings.Port);
                                    if (hostPolicy.Hosts.AllowsAny) logger.CreateLogger("ArgueBot.Service").LogWarning("AllowedHosts includes *: requests for any host name are accepted. Sign-in still protects the API; change the default administrator password.");
                                    Console.WriteLine("ArgueBot service: " + (settings.TlsEnabled ? "https" : "http") + "://127.0.0.1:" + settings.Port + " | home " + paths.Home);
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
                        }
                        if (database.OwnershipToken.IsCancellationRequested) throw new IOException("Exclusive database ownership was lost. The service stopped without reconnecting its writer.");
                    }
                }
            }
            }
            return restartRequested;
        }
    }
}
