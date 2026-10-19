namespace ArgueBot.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class Program
    {
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        private static async Task<int> Main(string[] args)
        {
            using (CancellationTokenSource lifetime = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
                try { return await ExecuteAsync(args, lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return 130; }
                catch (DomainException ex) { Console.Error.WriteLine(ex.Code + ": " + ex.Message); return ex.StatusCode == 400 ? 2 : 1; }
                catch (Exception ex) when (ex is IOException || ex is System.Net.Http.HttpRequestException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException || ex is TimeoutException || ex is FormatException)
                { Console.Error.WriteLine(ex.Message); return 1; }
            }
        }
        private static async Task<int> ExecuteAsync(string[] args, CancellationToken token)
        {
            List<string> words = new List<string>(); string? home = null; string? server = null; bool autoStart = true; string? session = Environment.GetEnvironmentVariable("ARGUEBOT_TOKEN");
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--home" && index + 1 < args.Length) home = args[++index];
                else if (args[index] == "--server" && index + 1 < args.Length) server = args[++index];
                else if (args[index] == "--no-start") autoStart = false;
                else if (args[index] == "--token-env" && index + 1 < args.Length) session = Environment.GetEnvironmentVariable(args[++index]);
                else words.Add(args[index]);
            }
            if (words.Count == 0 || words[0] == "--help" || words[0] == "help") { Help(); return 0; }
            if (words[0] == "service" && words.Count > 1 && words[1] != "start") autoStart = false;
            server = await LocalServiceConnector.ConnectAsync(new ArgueBotPaths(home).Home, server, autoStart, token).ConfigureAwait(false);
            using (ArgueBotClient client = new ArgueBotClient(server))
            {
                if (!string.IsNullOrWhiteSpace(session)) client.SetToken(session);
                else await client.PairLocalCachedAsync(new ArgueBotPaths(home).Home, token).ConfigureAwait(false);
                string command = words[0]; string operation = words.Count > 1 ? words[1] : "list";
                if (command == "service")
                {
                    if (operation == "status" || operation == "start") Print(await client.SendAsync<HealthResponse>(System.Net.Http.HttpMethod.Get, "health", token: token).ConfigureAwait(false));
                    else if (operation == "stop") Print(await client.SendAsync<Acknowledgement>(System.Net.Http.HttpMethod.Post, "v1.0/api/settings/shutdown", token: token).ConfigureAwait(false));
                    else if (operation == "restart") Print(await client.SendAsync<RestartResponse>(System.Net.Http.HttpMethod.Post, "v1.0/api/settings/restart", token: token).ConfigureAwait(false));
                    else { Help(); return 2; }
                }
                else if (command == "audit" && operation == "list")
                {
                    int page = words.Count > 3 ? int.Parse(words[3], System.Globalization.CultureInfo.InvariantCulture) : 1;
                    Print(await client.SendAsync<AuditPage>(System.Net.Http.HttpMethod.Get, "v1.0/api/audit?operation=" + Uri.EscapeDataString(words.Count > 2 ? words[2] : "") + "&pageNumber=" + page, token: token).ConfigureAwait(false));
                }
                else if (command == "backups")
                {
                    string root = "v1.0/api/backups";
                    if (operation == "list") Print(await client.SendAsync<BackupPage>(System.Net.Http.HttpMethod.Get, root, token: token).ConfigureAwait(false));
                    else if (operation == "operations") Print(await client.SendAsync<BackupOperationPage>(System.Net.Http.HttpMethod.Get, "v1.0/api/backup-operations" + (words.Count > 2 ? "?backupId=" + Uri.EscapeDataString(words[2]) : ""), token: token).ConfigureAwait(false));
                    else if (operation == "status") { Require(words, 3); Print(await client.SendAsync<BackupOperation>(System.Net.Http.HttpMethod.Get, "v1.0/api/backup-operations/" + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false)); }
                    else if (operation == "download") { Require(words, 4); await client.DownloadBackupAsync(words[2], words[3], token).ConfigureAwait(false); Print(new Acknowledgement()); }
                    else if (operation == "delete") { Require(words, 3); Print(await client.SendAsync<DeletionResponse>(System.Net.Http.HttpMethod.Delete, root + "/" + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false)); }
                    else if (operation == "create" || operation == "verify" || operation == "restore")
                    {
                        int inputIndex = operation == "create" ? 2 : 3;
                        if (operation != "create") Require(words, 3);
                        string json = await TextAsync(words, inputIndex, token).ConfigureAwait(false);
                        BackupRequest request = JsonSerializer.Deserialize<BackupRequest>(json, _Json) ?? throw new ArgumentException("Supply a recovery request JSON object.");
                        string path = operation == "create" ? root : root + "/" + Uri.EscapeDataString(words[2]) + "/" + operation;
                        Print(await client.SendAsync<BackupOperation>(System.Net.Http.HttpMethod.Post, path, request, token).ConfigureAwait(false));
                    }
                    else return 2;
                }
                else if (command == "conversations")
                {
                    switch (operation)
                    {
                        case "list": Print(await client.ConversationsAsync(words.Count > 2 ? words[2] : "", token: token).ConfigureAwait(false)); break;
                        case "create": Print(await client.CreateConversationAsync(await TextAsync(words, 2, token).ConfigureAwait(false), token).ConfigureAwait(false)); break;
                        case "show": Require(words, 3); Print(await client.MessagesAsync(words[2], token).ConfigureAwait(false)); break;
                        case "rename": Require(words, 4); Conversation renamed = await client.ConversationAsync(words[2], token).ConfigureAwait(false); renamed.Title = string.Join(" ", words.Skip(3)); Print(await client.UpdateConversationAsync(renamed, token).ConfigureAwait(false)); break;
                        case "archive": Require(words, 3); Conversation archived = await client.ConversationAsync(words[2], token).ConfigureAwait(false); archived.Archived = true; Print(await client.UpdateConversationAsync(archived, token).ConfigureAwait(false)); break;
                        case "export": Require(words, 3); List<Message> messages = await client.MessagesAsync(words[2], token).ConfigureAwait(false); foreach (Message message in messages) Console.WriteLine("## " + message.Speaker + " (" + message.Kind + ")\n\n" + message.Content + "\n"); break;
                        default: Help(); return 2;
                    }
                }
                else if (command == "participants")
                {
                    if (operation == "list") Print(await client.ParticipantsAsync(token).ConfigureAwait(false));
                    else if (operation == "delete") { Require(words, 3); Print(await client.SendAsync<DeletionResponse>(System.Net.Http.HttpMethod.Delete, "v1.0/api/participants/" + Uri.EscapeDataString(words[2]), token: token).ConfigureAwait(false)); }
                    else if (operation == "update")
                    {
                        Require(words, 3);
                        string json = await TextAsync(words, 3, token).ConfigureAwait(false);
                        RunnerProfile profile = JsonSerializer.Deserialize<RunnerProfile>(json, _Json) ?? throw new ArgumentException("Supply a participant JSON object.");
                        Print(await client.SendAsync<RunnerProfile>(System.Net.Http.HttpMethod.Put, "v1.0/api/participants/" + Uri.EscapeDataString(words[2]), profile, token).ConfigureAwait(false));
                    }
                    else if (operation == "add")
                    {
                        string json = await TextAsync(words, 2, token).ConfigureAwait(false);
                        RunnerProfile profile = JsonSerializer.Deserialize<RunnerProfile>(json, _Json) ?? throw new InvalidDataException("Profile must be a JSON object.");
                        Print(await client.CreateParticipantAsync(profile, token).ConfigureAwait(false));
                    }
                    else return 2;
                }
                else if (command == "message" && operation == "send")
                { Require(words, 3); Print(await client.NoteAsync(words[2], await TextAsync(words, 3, token).ConfigureAwait(false), token: token).ConfigureAwait(false)); }
                else if (command == "debate")
                {
                    Require(words, 3);
                    if (operation == "start")
                    {
                        Require(words, 5);
                        Print(await client.StartAsync(words[2], new CreateRunRequest { ParticipantIds = words.Skip(3).ToList(), IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false));
                    }
                    else if (operation == "status") Print(await client.RunsAsync(words[2], token).ConfigureAwait(false));
                    else
                    {
                        Require(words, 4);
                        await client.ActionAsync(words[2], words[3], new CommandRequest { Action = operation, IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                        Print(new { accepted = true });
                    }
                }
                else { Help(); return 2; }
            }
            return 0;
        }
        private static async Task<string> TextAsync(List<string> words, int index, CancellationToken token)
        {
            if (words.Count > index && words[index] == "--file") { Require(words, index + 2); return await File.ReadAllTextAsync(words[index + 1], token).ConfigureAwait(false); }
            if (words.Count > index && words[index] != "-") return string.Join(" ", words.Skip(index));
            return await Console.In.ReadToEndAsync(token).ConfigureAwait(false);
        }
        private static void Require(List<string> words, int count) { if (words.Count < count) throw new ArgumentException("Missing command arguments. Use --help."); }
        private static void Print(object value) { Console.WriteLine(JsonSerializer.Serialize(value, _Json)); }
        private static void Help()
        {
            Console.WriteLine("""
ArgueBot replacement CLI [--home PATH] [--server URL] [--token-env NAME]
  service start|status|restart|stop
  conversations list [SEARCH]       Search owned history (JSON).
  conversations create TITLE|-|--file PATH
  conversations show ID            Read ordered messages (JSON).
  conversations rename ID TITLE
  conversations archive ID
  conversations export ID          Markdown to stdout.
  audit list [OPERATION] [PAGE]    Inspect scoped immutable security events.
  backups list
  backups create JSON|-|--file PATH
  backups verify|restore BACKUP JSON|-|--file PATH
  backups operations [BACKUP]       List durable recovery jobs.
  backups status OPERATION
  backups download BACKUP PATH       Publish a complete archive to a new file.
  backups delete BACKUP
  participants list
  participants add JSON|-|--file PATH
  participants update ID JSON|-|--file PATH
  participants delete ID
  message send ID TEXT|-|--file PATH
  debate start CONVERSATION PROFILE PROFILE [PROFILE]
  debate status CONVERSATION
  debate pause|resume|interrupt|stop|extend CONVERSATION RUN
Local clients exchange the protected home pairing code. Remote clients use
ARGUEBOT_TOKEN or --token-env. Prompts and JSON may be supplied on stdin.
Exit codes: 0 success, 1 operation failure, 2 usage, 130 cancelled.
Local commands start or attach to the service automatically. --no-start requires
an existing service. Explicit --server URLs attach without starting processes.
""");
        }
    }
}
