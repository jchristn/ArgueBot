namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using Touchstone.Core;

    /// <summary>Local clients start and attach to a real service in a disposable home.</summary>
    public static class ServiceLifecycleSuites
    {
        /// <summary>Lifecycle descriptors require the solution's Release service build.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Service", "Local service lifecycle", new List<TestCaseDescriptor> { new TestCaseDescriptor("Service", "StartAttach", "Concurrent startup, rotated pairing, client detachment and shared history", StartAttachAsync) }) }; }
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
                await new ServiceSettings { Port = port }.SaveAsync(paths, token).ConfigureAwait(false);
                Task<string> first = LocalServiceConnector.ConnectAsync(home, token: token);
                Task<string> second = LocalServiceConnector.ConnectAsync(home, token: token);
                string[] endpoints = await Task.WhenAll(first, second).ConfigureAwait(false);
                if (endpoints[0] != endpoints[1]) throw new InvalidDataException("Concurrent clients attached to different listeners.");
                string descriptorFile = Path.Combine(home, "runtime", "service.json");
                for (int retry = 0; retry < 20 && !File.Exists(descriptorFile); retry++) await Task.Delay(50, token).ConfigureAwait(false);
                LocalServiceDescriptor descriptor = JsonSerializer.Deserialize<LocalServiceDescriptor>(await File.ReadAllTextAsync(descriptorFile, token).ConfigureAwait(false)) ?? throw new InvalidDataException("Service did not publish attachment metadata.");
                service = Process.GetProcessById(descriptor.ProcessId);
                using (ArgueBotClient one = new ArgueBotClient(endpoints[0]))
                using (ArgueBotClient two = new ArgueBotClient(endpoints[1]))
                {
                    await Task.WhenAll(one.PairLocalAsync(home, token), two.PairLocalAsync(home, token)).ConfigureAwait(false);
                    Conversation conversation = await one.CreateConversationAsync("Shared startup fixture", token).ConfigureAwait(false);
                    await two.NoteAsync(conversation.Id, "Seen by a second client", token: token).ConfigureAwait(false);
                    if ((await one.MessagesAsync(conversation.Id, token).ConfigureAwait(false)).Count != 1) throw new InvalidDataException("Clients did not share durable history.");
                }
                if (service.HasExited || await LocalServiceConnector.ConnectAsync(home, autoStart: false, token: token).ConfigureAwait(false) != endpoints[0]) throw new InvalidDataException("Closing clients stopped the service.");
                using (ArgueBotClient admin = new ArgueBotClient(endpoints[0]))
                {
                    await admin.PairLocalAsync(home, token).ConfigureAwait(false);
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
                if (Directory.Exists(home)) Directory.Delete(home, true);
            }
        }
    }
}
