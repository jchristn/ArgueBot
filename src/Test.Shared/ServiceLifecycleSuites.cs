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
    using ArgueBot.Core.Backups;
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
            get { return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Service", "Local service lifecycle", new List<TestCaseDescriptor> { new TestCaseDescriptor("Service", "StartAttach", "Concurrent startup, rotated pairing, client detachment and shared history", StartAttachAsync) }) }; }
        }
        private static async Task<BackupOperation> AwaitRecoveryAsync(ArgueBotClient client, string id, CancellationToken token)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                while (true)
                {
                    BackupOperation operation = await client.SendAsync<BackupOperation>(HttpMethod.Get, "v1.0/api/backup-operations/" + id, token: deadline.Token).ConfigureAwait(false);
                    if (operation.State == "completed" || operation.State == "failed") return operation;
                    await Task.Delay(25, deadline.Token).ConfigureAwait(false);
                }
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
                await new ServiceSettings { Port = port }.SaveAsync(paths, token).ConfigureAwait(false);
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
                await new ServiceSettings { Port = port }.SaveAsync(new ArgueBotPaths(otherHome), token).ConfigureAwait(false);
                bool wrongHome = false;
                try { await LocalServiceConnector.ConnectAsync(otherHome, token: token).ConfigureAwait(false); }
                catch (InvalidOperationException exception) when (exception.Message.Contains("different application home", StringComparison.Ordinal)) { wrongHome = true; }
                if (!wrongHome) throw new InvalidDataException("A second home attached to another home's healthy listener.");
                using (ArgueBotClient one = new ArgueBotClient(endpoints[0]))
                using (ArgueBotClient two = new ArgueBotClient(endpoints[1]))
                {
                    await Task.WhenAll(one.PairLocalAsync(home, token), two.PairLocalAsync(home, token)).ConfigureAwait(false);
                    Conversation conversation = await one.CreateConversationAsync("Shared startup fixture", token).ConfigureAwait(false);
                    await two.NoteAsync(conversation.Id, "Seen by a second client", token: token).ConfigureAwait(false);
                    bool rejected = false;
                    try { await one.CreateParticipantAsync(new RunnerProfile { Name = "invalid credential URL capture fixture", Kind = "rest", Provider = "ollama", Endpoint = "http://fixture-user:fixture-secret-url@127.0.0.1:11434/?token=fixture-secret-query", Model = "fixture" }, token).ConfigureAwait(false); }
                    catch (DomainException ex) when (ex.StatusCode == 400) { rejected = true; }
                    if (!rejected) throw new InvalidDataException("Credential URL configuration was accepted.");
                    rejected = false;
                    try { await one.SendAsync<BackupOperation>(HttpMethod.Post, "v1.0/api/backups/bak_fixture/restore", new BackupRequest { DestinationName = "bad-reference", RestoreDatabaseType = ArgueBot.Core.Database.DatabaseTypeEnum.Postgresql, RestoreConnectionEnvironmentVariable = "Host=127.0.0.1;Password=fixture-secret-connection" }, token).ConfigureAwait(false); }
                    catch (DomainException exception) when (exception.StatusCode == 400) { rejected = true; }
                    if (!rejected) throw new InvalidDataException("A literal connection credential was accepted as an environment reference.");
                    RequestHistoryEntry? captured = null;
                    for (int retry = 0; retry < 20 && captured == null; retry++)
                    {
                        HistoryPage history = await one.SendAsync<HistoryPage>(HttpMethod.Get, "v1.0/api/request-history?method=POST&status=400", token: token).ConfigureAwait(false);
                        RequestHistoryEntry? record = history.Items.FirstOrDefault(item => item.Path == "/v1.0/api/participants");
                        if (record != null) captured = await one.SendAsync<RequestHistoryEntry>(HttpMethod.Get, "v1.0/api/request-history/" + record.Id, token: token).ConfigureAwait(false);
                        else await Task.Delay(50, token).ConfigureAwait(false);
                    }
                    RequestHistoryEntry? invalidReference = null;
                    for (int retry = 0; retry < 20 && invalidReference == null; retry++)
                    {
                        HistoryPage history = await one.SendAsync<HistoryPage>(HttpMethod.Get, "v1.0/api/request-history?method=POST&status=400", token: token).ConfigureAwait(false);
                        RequestHistoryEntry? record = history.Items.FirstOrDefault(item => item.Path == "/v1.0/api/backups/bak_fixture/restore");
                        if (record != null) invalidReference = await one.SendAsync<RequestHistoryEntry>(HttpMethod.Get, "v1.0/api/request-history/" + record.Id, token: token).ConfigureAwait(false);
                        else await Task.Delay(50, token).ConfigureAwait(false);
                    }
                    if (invalidReference == null || invalidReference.RawRequestBody.Contains("fixture-secret", StringComparison.Ordinal)) throw new InvalidDataException("An invalid credential reference leaked its literal connection value into request history.");
                    if (captured == null || captured.RawRequestBody.Contains("fixture-secret", StringComparison.Ordinal) || captured.Headers.Any(header => header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && header.Value != "[redacted]")) throw new InvalidDataException("Credential URL or authorization data leaked into captured request diagnostics.");
                    BackupOperation created = await one.SendAsync<BackupOperation>(HttpMethod.Post, "v1.0/api/backups", new BackupRequest { Name = "HTTP recovery fixture", Password = "fixture-recovery-passphrase" }, token).ConfigureAwait(false);
                    created = await AwaitRecoveryAsync(two, created.Id, token).ConfigureAwait(false);
                    if (created.State != "completed") throw new InvalidDataException("Service-owned archive creation failed: " + created.ErrorCode);
                    BackupEntry artifact = await two.SendAsync<BackupEntry>(HttpMethod.Get, "v1.0/api/backups/" + created.BackupId, token: token).ConfigureAwait(false);
                    if (artifact.Status != "ready" || artifact.Bytes <= 0 || artifact.Digest.Length != 64) throw new InvalidDataException("Archive publication metadata is incomplete.");
                    string download = Path.Combine(home, "downloaded.arguebak");
                    await two.DownloadBackupAsync(artifact.Id, download, token).ConfigureAwait(false);
                    using (FileStream downloaded = File.OpenRead(download))
                    {
                        string digest = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(downloaded, token).ConfigureAwait(false)).ToLowerInvariant();
                        if (digest != artifact.Digest) throw new InvalidDataException("Authenticated archive download differs from the published checksum.");
                    }
                    using (VerifiedRecovery recovery = await RecoveryArchive.OpenAsync(Path.Combine(home, "backups", artifact.Id + ".arguebak"), "fixture-recovery-passphrase", token: token).ConfigureAwait(false))
                    { if (recovery.Manifest.Dependencies.Count < 10) throw new InvalidDataException("Archive omitted the embedded dependency inventory."); }
                    BackupOperation wrongKey = await two.SendAsync<BackupOperation>(HttpMethod.Post, "v1.0/api/backups/" + artifact.Id + "/verify", new BackupRequest { Password = "fixture-wrong-passphrase" }, token).ConfigureAwait(false);
                    wrongKey = await AwaitRecoveryAsync(one, wrongKey.Id, token).ConfigureAwait(false);
                    if (wrongKey.State != "failed" || wrongKey.ErrorCode != "archive_authentication_failed") throw new InvalidDataException("Wrong archive key did not fail safely.");
                    BackupOperation restore = await two.SendAsync<BackupOperation>(HttpMethod.Post, "v1.0/api/backups/" + artifact.Id + "/restore", new BackupRequest { Password = "fixture-recovery-passphrase", DestinationName = "http-recovered", Port = 4547 }, token).ConfigureAwait(false);
                    restore = await AwaitRecoveryAsync(one, restore.Id, token).ConfigureAwait(false);
                    if (restore.State != "completed" || !File.Exists(Path.Combine(home, "restores", "http-recovered", "arguebot.db"))) throw new InvalidDataException("Controlled new-home restore failed: " + restore.ErrorCode);
                    BackupOperationPage jobs = await two.SendAsync<BackupOperationPage>(HttpMethod.Get, "v1.0/api/backup-operations?backupId=" + Uri.EscapeDataString(artifact.Id) + "&pageSize=1", token: token).ConfigureAwait(false);
                    if (jobs.TotalCount != 3 || jobs.Items.Single().Id != restore.Id || jobs.PageSize != 1) throw new InvalidDataException("The HTTP operation list cannot recover the latest durable job.");
                    await two.SendAsync<DeletionResponse>(HttpMethod.Delete, "v1.0/api/backups/" + artifact.Id, token: token).ConfigureAwait(false);
                    if (File.Exists(Path.Combine(home, "backups", artifact.Id + ".arguebak"))) throw new InvalidDataException("Artifact deletion retained its file.");
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
