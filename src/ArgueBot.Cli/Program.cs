namespace ArgueBot.Cli
{
    using ArgueBot.Client;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        private static readonly JsonSerializerOptions _JsonLine = new JsonSerializerOptions(_Json) { WriteIndented = false };
        private static async Task<int> Main(string[] args)
        {
            Console.InputEncoding = new UTF8Encoding(false, true);
            Console.OutputEncoding = new UTF8Encoding(false, true);
            using (CancellationTokenSource lifetime = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
                try { return await ExecuteAsync(args, lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return 130; }
                catch (DomainException ex) { Console.Error.WriteLine(ex.Code + (ex.Field.Length > 0 ? " (" + ex.Field + ")" : "") + ": " + ex.Message); return ex.StatusCode == 400 ? 2 : 1; }
                catch (Exception ex) when (ex is IOException || ex is System.Net.Http.HttpRequestException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException || ex is TimeoutException || ex is FormatException)
                { Console.Error.WriteLine(ex.Message); return 1; }
            }
        }
        private static async Task<int> ExecuteAsync(string[] args, CancellationToken token)
        {
            List<string> words = new List<string>(); string? home = null; string? server = null; int? port = null; bool autoStart = true;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--home" && index + 1 < args.Length) home = args[++index];
                else if (args[index] == "--server" && index + 1 < args.Length) server = args[++index];
                else if (args[index] == "--port" && index + 1 < args.Length) port = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                else if (args[index] == "--no-start") autoStart = false;
                else words.Add(args[index]);
            }
            if (words.Count == 0 || words.Contains("--help") || words[0] == "help") { Help(); return 0; }
            bool web = words[0] == "service" && words.Count == 2 && words[1] == "web";
            if (port.HasValue && !web) throw new ArgumentException("--port is supported by service web for starting a local dashboard service.");
            if (words[0] == "service" && words.Count > 1 && words[1] != "start" && !web) autoStart = false;
            if (words.Count > 1 && words[0] == "service" && words[1] == "stop" && server == null && !File.Exists(Path.Combine(new ArgueBotPaths(home).Home, "runtime", "service.json")))
            { Console.WriteLine("ArgueBot is already stopped."); return 0; }
            try { server = await LocalServiceConnector.ConnectAsync(new ArgueBotPaths(home).Home, server, autoStart, token, port).ConfigureAwait(false); }
            catch (InvalidOperationException exception) when (words.Count > 1 && words[0] == "service" && words[1] == "stop" && exception.Message.StartsWith("The local service is not running.", StringComparison.Ordinal))
            { Console.WriteLine("ArgueBot is already stopped."); return 0; }
            using (ArgueBotClient client = LocalServiceConnector.CreateClient(server, new ArgueBotPaths(home).Home))
            {
                if (web)
                {
                    string dashboard = new Uri(new Uri(server.TrimEnd('/') + "/"), "dashboard").AbsoluteUri;
                    Console.WriteLine("Open ArgueBot in your browser: " + dashboard);
                    Console.WriteLine("The service is running in the background. You can close this terminal.");
                    return 0;
                }
                string command = words[0]; string operation = words.Count > 1 ? words[1] : "list";
                if (command == "service")
                {
                    if (operation == "status" || operation == "start") Print(await client.SendAsync<HealthResponse>(System.Net.Http.HttpMethod.Get, "health", token: token).ConfigureAwait(false));
                    else if (operation == "stop")
                    {
                        Acknowledgement response = await client.SendAsync<Acknowledgement>(System.Net.Http.HttpMethod.Post, "v1.0/api/settings/shutdown", token: token).ConfigureAwait(false);
                        await WaitForShutdownAsync(client, token).ConfigureAwait(false);
                        Print(response);
                    }
                    else if (operation == "restart") Print(await client.SendAsync<RestartResponse>(System.Net.Http.HttpMethod.Post, "v1.0/api/settings/restart", token: token).ConfigureAwait(false));
                    else { Help(); return 2; }
                }
                else if (command == "conversations")
                {
                    switch (operation)
                    {
                        case "list": Print(await client.ConversationsAsync(words.Count > 2 ? words[2] : "", token: token).ConfigureAwait(false)); break;
                        case "create": Print(await client.CreateConversationAsync(await TextAsync(words, 2, token).ConfigureAwait(false), token).ConfigureAwait(false)); break;
                        case "show":
                            Require(words, 3);
                            if (words.Contains("--jsonl")) { await foreach (Message message in client.EnumerateMessagesAsync(words[2], token).ConfigureAwait(false)) Console.WriteLine(JsonSerializer.Serialize(message, _JsonLine)); }
                            else Print(await client.MessagesAsync(words[2], token).ConfigureAwait(false));
                            break;
                        case "rename": Require(words, 4); Conversation renamed = await client.ConversationAsync(words[2], token).ConfigureAwait(false); renamed.Title = string.Join(" ", words.Skip(3)); Print(await client.UpdateConversationAsync(renamed, token).ConfigureAwait(false)); break;
                        case "archive": Require(words, 3); Conversation archived = await client.ConversationAsync(words[2], token).ConfigureAwait(false); archived.Archived = true; Print(await client.UpdateConversationAsync(archived, token).ConfigureAwait(false)); break;
                        case "export": Require(words, 3); await foreach (Message message in client.EnumerateMessagesAsync(words[2], token).ConfigureAwait(false)) Console.WriteLine("## " + message.Speaker + " (" + message.Kind + ")\n\n" + MessagePresentation.Markdown(message) + "\n"); break;
                        default: Help(); return 2;
                    }
                }
                else if (command == "configuration")
                {
                    Require(words, 3); string resource = words[1]; string action = words[2];
                    if (resource != "endpoints" && resource != "harnesses" && resource != "models" && resource != "credentials") throw new ArgumentException("Choose endpoints, harnesses, models or credentials.");
                    string path = "v2.0/api/configuration/" + resource;
                    if (resource == "models" && action == "checks") { Require(words, 4); Print(await client.ModelChecksAsync(words[3], words.Count > 4 ? int.Parse(words[4], System.Globalization.CultureInfo.InvariantCulture) : 1, token).ConfigureAwait(false)); }
                    else if (resource == "models" && action == "check") { Require(words, 5); ModelCheckRequest request = JsonSerializer.Deserialize<ModelCheckRequest>(await TextAsync(words, 4, token).ConfigureAwait(false), _Json) ?? throw new ArgumentException("Supply a model-check JSON request."); Print(await client.CheckModelAsync(words[3], request, token).ConfigureAwait(false)); }
                    else if (action == "list")
                    {
                        if (resource == "endpoints") Print(await client.EndpointsAsync(token).ConfigureAwait(false));
                        else if (resource == "harnesses") Print(await client.HarnessesAsync(token).ConfigureAwait(false));
                        else if (resource == "models") Print(await client.ModelsAsync(token).ConfigureAwait(false));
                        else Print(await client.CredentialsAsync(token).ConfigureAwait(false));
                    }
                    else if (resource == "endpoints" && action == "preview") { Require(words, 4); Print(await client.PreviewEndpointAsync(words[3], token).ConfigureAwait(false)); }
                    else if (resource == "endpoints" && action == "discover") { Require(words, 4); Print(await client.DiscoverEndpointAsync(words[3], token).ConfigureAwait(false)); }
                    else if (action == "add" || action == "update")
                    {
                        int bodyIndex = action == "add" ? 3 : 4;
                        Require(words, bodyIndex + 1);
                        if (resource == "credentials" && words[bodyIndex] != "-" && words[bodyIndex] != "--file") throw new ArgumentException("Read credential JSON from stdin (-) or --file PATH so secrets do not enter shell history or process arguments.");
                        string body = await TextAsync(words, bodyIndex, token).ConfigureAwait(false);
                        JsonElement document;
                        try { document = JsonSerializer.Deserialize<JsonElement>(body); }
                        catch (JsonException) { throw new ArgumentException("Supply a complete valid JSON configuration document. Input values are omitted."); }
                        Print(await client.SendAsync<JsonElement>(action == "add" ? System.Net.Http.HttpMethod.Post : System.Net.Http.HttpMethod.Put, path + (action == "add" ? "" : "/" + Uri.EscapeDataString(words[3])), document, token).ConfigureAwait(false));
                    }
                    else if (resource == "credentials" && action == "reveal") { Require(words, 4); Print(await client.RevealCredentialAsync(words[3], token).ConfigureAwait(false)); }
                    else if (resource == "credentials" && action == "disable") { Require(words, 5); Print(await client.DisableCredentialAsync(words[3], long.Parse(words[4], System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false)); }
                    else throw new ArgumentException("Use list, add, update or reveal. Credential disable also requires ID and revision.");
                }
                else if (command == "credential-keys")
                {
                    if (operation == "status") Print(await client.CredentialKeysAsync(token).ConfigureAwait(false));
                    else if (operation == "rotate") { Require(words, 3); Print(await client.RotateCredentialKeyAsync(words[2], token).ConfigureAwait(false)); }
                    else if (operation == "retire") { Require(words, 3); Print(await client.RetireCredentialKeysAsync(words[2], token).ConfigureAwait(false)); }
                    else throw new ArgumentException("Use credential-keys status, rotate EXPECTED_KEY_ID or retire EXPECTED_KEY_ID.");
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
                else if (command == "capabilities") Print(await client.CapabilitiesAsync(token).ConfigureAwait(false));
                else if (command == "interject")
                {
                    Require(words, 3);
                    InterjectionRequest request = JsonSerializer.Deserialize<InterjectionRequest>(await TextAsync(words, 2, token).ConfigureAwait(false), _Json) ?? throw new ArgumentException("Supply context with deliveryMode: interrupt_now or next_turn.");
                    if (request.IdempotencyKey.Length == 0) request.IdempotencyKey = Guid.NewGuid().ToString("N");
                    Print(await client.InterjectAsync(operation, request, token).ConfigureAwait(false));
                }
                else if (command == "message" && operation == "send")
                { Require(words, 3); Print(await client.NoteAsync(words[2], await TextAsync(words, 3, token).ConfigureAwait(false), token: token).ConfigureAwait(false)); }
                else if (command == "debate")
                {
                    Require(words, 3);
                    if (operation == "start")
                    {
                        Require(words, 4);
                        CreateRunRequest request;
                        if (words[3] == "-" || words[3] == "--file") request = JsonSerializer.Deserialize<CreateRunRequest>(await TextAsync(words, 3, token).ConfigureAwait(false), _Json) ?? throw new ArgumentException("Supply a run request JSON object.");
                        else { Require(words, 5); request = new CreateRunRequest { ParticipantIds = words.Skip(3).ToList() }; }
                        if (request.IdempotencyKey.Length == 0) request.IdempotencyKey = Guid.NewGuid().ToString("N");
                        Print(await client.StartAsync(words[2], request, token).ConfigureAwait(false));
                    }
                    else if (operation == "summary") { Require(words, 5); Print(await client.SummaryAsync(words[2], words[3], words[4], words.Count > 5 ? int.Parse(words[5], System.Globalization.CultureInfo.InvariantCulture) : 1, token).ConfigureAwait(false)); }
                    else if (operation == "summaries") { Require(words, 4); Print(await client.SummariesAsync(words[2], words[3], words.Count > 4 ? int.Parse(words[4], System.Globalization.CultureInfo.InvariantCulture) : 1, token: token).ConfigureAwait(false)); }
                    else if (operation == "clarifications") { Require(words, 4); Print(await client.ClarificationsAsync(words[2], words[3], token).ConfigureAwait(false)); }
                    else if (operation == "roster") { Require(words, 4); Print(await client.RosterAsync(words[2], words[3], token).ConfigureAwait(false)); }
                    else if (operation == "proposals") { Require(words, 4); Print(await client.ProposalsAsync(words[2], words[3], token).ConfigureAwait(false)); }
                    else if (operation == "interjections") Print(await client.InterjectionsAsync(words[2], words.Count > 3 ? int.Parse(words[3], System.Globalization.CultureInfo.InvariantCulture) : 1, token: token).ConfigureAwait(false));
                    else if (operation == "change-roster")
                    {
                        Require(words, 5);
                        RosterChangeRequest change = JsonSerializer.Deserialize<RosterChangeRequest>(await TextAsync(words, 4, token).ConfigureAwait(false), _Json) ?? throw new ArgumentException("Supply a roster change with the expected run revision.");
                        if (change.IdempotencyKey.Length == 0) change.IdempotencyKey = Guid.NewGuid().ToString("N");
                        Print(await client.ChangeRosterAsync(words[2], words[3], change, token).ConfigureAwait(false));
                    }
                    else if (operation == "attempts") { Require(words, 4); Print(await client.AttemptsAsync(words[2], words[3], words.Count > 4 ? int.Parse(words[4], System.Globalization.CultureInfo.InvariantCulture) : 1, token: token).ConfigureAwait(false)); }
                    else if (operation == "status") Print(await client.RunsAsync(words[2], token).ConfigureAwait(false));
                    else if (operation == "edit-proposal")
                    {
                        Require(words, 6);
                        long revision = long.Parse(words[4], System.Globalization.CultureInfo.InvariantCulture);
                        await client.ActionAsync(words[2], words[3], new CommandRequest { Action = "edit_proposal", Revision = revision, Text = await TextAsync(words, 5, token).ConfigureAwait(false), IdempotencyKey = Guid.NewGuid().ToString("N") }, token).ConfigureAwait(false);
                        Print(new { accepted = true });
                    }
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
        private static async Task WaitForShutdownAsync(ArgueBotClient client, CancellationToken token)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    while (true)
                    {
                        await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                        try { await client.SendAsync<HealthResponse>(System.Net.Http.HttpMethod.Get, "health", token: deadline.Token).ConfigureAwait(false); }
                        catch (System.Net.Http.HttpRequestException) { return; }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new TimeoutException("ArgueBot accepted shutdown but has not stopped after 15 seconds."); }
            }
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
ArgueBot CLI [--home PATH] [--server URL] [--no-start] COMMAND

Service
  service start|status|restart|stop
  service web [--port N]                     Start or attach, then print the dashboard URL.
  capabilities                               Limits, adapters and supported protocol versions.

Conversations
  conversations list [SEARCH]                Search conversation history (JSON).
  conversations create TITLE|-|--file PATH
  conversations show ID [--jsonl]            Full ordered messages; --jsonl streams them.
  conversations rename ID TITLE
  conversations archive ID
  conversations export ID                    Markdown to stdout.
  message send ID TEXT|-|--file PATH         Ask a single participant a follow-up.

Debates
  debate start CONVERSATION -|--file PATH    Panel JSON: protocolVersion 2, orchestratorId,
                                             participantIds and optional policy.
  debate start CONVERSATION PROFILE PROFILE [PROFILE]   Legacy two or three agent debate.
  debate status CONVERSATION
  debate pause|resume|interrupt|stop|extend CONVERSATION RUN
  debate change-roster CONVERSATION RUN -|--file PATH   add, remove or replace a seat.
  debate roster|proposals|clarifications CONVERSATION RUN
  debate edit-proposal CONVERSATION RUN REVISION TEXT|-|--file PATH
                                             Replace the proposal under vote; every seat
                                             votes again. REVISION comes from debate status.
  debate attempts CONVERSATION RUN [PAGE]    Execution and context provenance.
  debate interjections CONVERSATION [PAGE]
  interject CONVERSATION -|--file PATH       Context JSON. deliveryMode is required:
                                             interrupt_now or next_turn. Optional pinned,
                                             recipientId, and clarificationId with
                                             skipClarification to answer or skip a question.

Memory
  debate summaries CONVERSATION RUN [PAGE]
  debate summary CONVERSATION RUN SUMMARY_ID [SOURCE_PAGE]
  debate rebuild_summary|exclude_summary CONVERSATION RUN

Configuration
  participants list
  participants add|update [ID] JSON|-|--file PATH
  participants delete ID
  configuration endpoints|harnesses|models|credentials list
  configuration endpoints|harnesses|models|credentials add -|--file PATH
  configuration endpoints|harnesses|models|credentials update ID -|--file PATH
  configuration credentials reveal ID          Print a ready credential's decrypted value
  configuration credentials disable ID REVISION
  configuration endpoints preview ID         No outbound request or credential resolution.
  configuration endpoints discover ID        Contacts the endpoint; no completion.
  configuration models check ID -|--file PATH   Invokes the model and may incur cost.
  configuration models checks ID [PAGE]
  credential-keys status
  credential-keys rotate EXPECTED_KEY_ID
  credential-keys retire EXPECTED_KEY_ID   Remove earlier keys once no credential uses them.
                                             Earlier database copies then lose their credentials.

The workspace is open: no accounts, sign-in or tokens. Prompts and JSON may be
supplied on stdin. Credential values are accepted only from stdin or --file.
Local commands start or attach to the service automatically; --no-start requires
an existing service, and an explicit --server URL attaches without starting one.
Exit codes: 0 success, 1 operation failure, 2 usage, 130 cancelled.
""");
        }
    }
}
