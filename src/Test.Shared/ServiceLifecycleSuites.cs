namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using Touchstone.Core;

    /// <summary>Local clients start and attach to a real service in a disposable home.</summary>
    public static class ServiceLifecycleSuites
    {
        /// <summary>Lifecycle descriptors require the solution's Release service build.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Service", "Local service lifecycle", new List<TestCaseDescriptor> { new TestCaseDescriptor("Service", "SessionCarriers", "Open workspace removes identity routes and ignores obsolete authentication settings", SessionCarriersAsync), new TestCaseDescriptor("Service", "StartAttach", "Concurrent startup, client detachment and shared open-workspace history", StartAttachAsync) }) }; }
        }
        private static async Task SessionCarriersAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-http-auth-" + Guid.NewGuid().ToString("N"));
            Process? service = null;
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                CertificateFixture.Create(new ArgueBotPaths(home));
                await new ServiceSettings { RequireAuthentication = true, Port = port, TlsEnabled = true, RequestHistoryEnabled = false }.SaveAsync(new ArgueBotPaths(home), token).ConfigureAwait(false);
                await File.WriteAllTextAsync(new ArgueBotPaths(home).SettingsFile, "{\"port\":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"tlsEnabled\":true,\"requireAuthentication\":true}", token).ConfigureAwait(false);
                string endpoint = await LocalServiceConnector.ConnectAsync(home, token: token).ConfigureAwait(false);
                string descriptorFile = Path.Combine(home, "runtime", "service.json");
                for (int retry = 0; retry < 40 && !File.Exists(descriptorFile); retry++) await Task.Delay(25, token).ConfigureAwait(false);
                LocalServiceDescriptor descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(descriptorFile, token).ConfigureAwait(false)) ?? throw new InvalidDataException("No service descriptor.");
                service = Process.GetProcessById(descriptor.ProcessId);
                using (ArgueBotClient client = LocalServiceConnector.CreateClient(endpoint, home))
                {
                    HealthResponse health = await client.SendAsync<HealthResponse>(HttpMethod.Get, "health", token: token).ConfigureAwait(false);
                    if (health.AuthenticationRequired || File.Exists(Path.Combine(home, "runtime", "pairing-code"))) throw new InvalidDataException("Open workspace exposed enrollment.");
                    foreach (string route in new[] { "auth/me", "auth/pair", "auth/login", "auth/logout", "users", "roles", "permissions", "audit" })
                    {
                        bool removed = false;
                        try { await client.SendAsync<object>(route.StartsWith("auth/", StringComparison.Ordinal) && route != "auth/me" ? HttpMethod.Post : HttpMethod.Get, "v1.0/api/" + route, token: token).ConfigureAwait(false); }
                        catch (DomainException exception) when (exception.StatusCode == 404) { removed = true; }
                        if (!removed) throw new InvalidDataException("Identity route remained exposed: " + route);
                    }
                    await client.CreateConversationAsync("No identity required", token).ConfigureAwait(false);
                    await client.SendAsync<Acknowledgement>(HttpMethod.Post, "v1.0/api/settings/shutdown", token: token).ConfigureAwait(false);
                }
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) { deadline.CancelAfter(TimeSpan.FromSeconds(5)); await service.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            }
            finally
            {
                await StopFixtureServiceAsync(home, service).ConfigureAwait(false);
                await DeleteStoppedHomeAsync(home).ConfigureAwait(false);
            }
        }
        private static async Task StopFixtureServiceAsync(string home, Process? service)
        {
            // Readiness can fail after the owned child publishes its descriptor. Capture that
            // process before deleting the fixture so cleanup cannot hide the startup failure.
            if (service == null)
            {
                string file = Path.Combine(home, "runtime", "service.json");
                if (!File.Exists(file)) return;
                LocalServiceDescriptor? descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(file).ConfigureAwait(false));
                if (descriptor == null || descriptor.HomeId != LocalHomeIdentity.Read(new ArgueBotPaths(home))) throw new InvalidDataException("Unverified fixture service descriptor; retain its home for investigation.");
                try { service = Process.GetProcessById(descriptor.ProcessId); }
                catch (ArgumentException) { return; }
                TimeSpan age = descriptor.StartedUtc - service.StartTime.ToUniversalTime();
                if (age < TimeSpan.FromSeconds(-1) || age > TimeSpan.FromSeconds(30)) { service.Dispose(); throw new InvalidDataException("Fixture process identity changed; retain its home for investigation."); }
            }
            try { if (!service.HasExited) service.Kill(true); await service.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            finally { service.Dispose(); }
        }
        internal static async Task DeleteStoppedHomeAsync(string home)
        {
            // Windows file scanners can briefly retain a handle after the child process exits.
            // Keep the cleanup bounded and propagate the final failure rather than hiding it.
            for (int retry = 0; Directory.Exists(home); retry++)
            {
                try { Directory.Delete(home, true); return; }
                catch (IOException) when (retry < 20) { await Task.Delay(100).ConfigureAwait(false); }
            }
        }
        private static async Task StartAttachAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-lifecycle-" + Guid.NewGuid().ToString("N"));
            Process? service = null;
            try
            {
                int port;
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                ArgueBotPaths paths = new ArgueBotPaths(home);
                CertificateFixture.Create(paths);
                await new ServiceSettings { RequireAuthentication = true, Port = port, TlsEnabled = true, EventMaxPerConversation = 10, EventMaintenanceSeconds = 5 }.SaveAsync(paths, token).ConfigureAwait(false);
                Task<string> first = LocalServiceConnector.ConnectAsync(home, token: token);
                Task<string> second = LocalServiceConnector.ConnectAsync(home, token: token);
                string[] endpoints = await Task.WhenAll(first, second).ConfigureAwait(false);
                if (endpoints[0] != endpoints[1]) throw new InvalidDataException("Concurrent clients attached to different listeners.");
                string descriptorFile = Path.Combine(home, "runtime", "service.json");
                for (int retry = 0; retry < 20 && !File.Exists(descriptorFile); retry++) await Task.Delay(50, token).ConfigureAwait(false);
                LocalServiceDescriptor descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(descriptorFile, token).ConfigureAwait(false)) ?? throw new InvalidDataException("Service did not publish attachment metadata.");
                service = Process.GetProcessById(descriptor.ProcessId);
                if (descriptor.HomeId != LocalHomeIdentity.Read(paths)) throw new InvalidDataException("Attachment metadata lost its home binding.");
                string otherHome = Path.Combine(home, "different-home");
                await new ServiceSettings { RequireAuthentication = true, Port = port, TlsEnabled = true }.SaveAsync(new ArgueBotPaths(otherHome), token).ConfigureAwait(false);
                bool wrongHome = false;
                try { await LocalServiceConnector.ConnectAsync(otherHome, autoStart: false, token: token).ConfigureAwait(false); }
                catch (InvalidOperationException) { wrongHome = true; }
                if (!wrongHome) throw new InvalidDataException("A second home attached to another home's healthy listener.");
                using (ArgueBotClient one = LocalServiceConnector.CreateClient(endpoints[0], home))
                using (ArgueBotClient two = LocalServiceConnector.CreateClient(endpoints[1], home))
                {
                    await MessageHttpChecks.RunAsync(one, token).ConfigureAwait(false);
                    Conversation conversation = await one.CreateConversationAsync("Shared startup fixture", token).ConfigureAwait(false);
                    await two.NoteAsync(conversation.Id, "Seen by a second client", token: token).ConfigureAwait(false);
                    bool rejected = false;
                    try { await one.CreateParticipantAsync(new RunnerProfile { Name = "invalid credential URL capture fixture", Kind = "rest", Provider = "ollama", Endpoint = "http://fixture-user:fixture-secret-url@127.0.0.1:11434/?token=fixture-secret-query", Model = "fixture" }, token).ConfigureAwait(false); }
                    catch (DomainException ex) when (ex.StatusCode == 400) { rejected = true; }
                    if (!rejected) throw new InvalidDataException("Credential URL configuration was accepted.");
                    RequestHistoryEntry? captured = null;
                    for (int retry = 0; retry < 20 && captured == null; retry++)
                    {
                        HistoryPage history = await one.SendAsync<HistoryPage>(HttpMethod.Get, "v1.0/api/request-history?method=POST&status=400", token: token).ConfigureAwait(false);
                        RequestHistoryEntry? record = history.Items.FirstOrDefault(item => item.Path == "/v1.0/api/participants");
                        if (record != null) captured = await one.SendAsync<RequestHistoryEntry>(HttpMethod.Get, "v1.0/api/request-history/" + record.Id, token: token).ConfigureAwait(false);
                        else await Task.Delay(50, token).ConfigureAwait(false);
                    }
                    if (captured == null || captured.RawRequestBody.Contains("fixture-secret", StringComparison.Ordinal) || captured.Headers.Any(header => header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && header.Value != "[redacted]")) throw new InvalidDataException("Credential URL or authorization data leaked into captured request diagnostics.");
                    if ((await one.MessagesAsync(conversation.Id, token).ConfigureAwait(false)).Count != 1) throw new InvalidDataException("Clients did not share durable history.");
                }
                if (service.HasExited || await LocalServiceConnector.ConnectAsync(home, autoStart: false, token: token).ConfigureAwait(false) != endpoints[0]) throw new InvalidDataException("Closing clients stopped the service.");
                using (ArgueBotClient admin = LocalServiceConnector.CreateClient(endpoints[0], home))
                {
                    await admin.SendAsync<Acknowledgement>(System.Net.Http.HttpMethod.Post, "v1.0/api/settings/shutdown", token: token).ConfigureAwait(false);
                }
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    await service.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                if (File.Exists(descriptorFile)) throw new InvalidDataException("Graceful shutdown retained stale attachment metadata.");
            }
            finally
            {
                if (service != null)
                {
                    if (!service.HasExited) service.Kill(true);
                    await service.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); service.Dispose();
                }
                await DeleteStoppedHomeAsync(home).ConfigureAwait(false);
            }
        }
    }
}
