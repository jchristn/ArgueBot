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
                catch (DomainException ex)
                {
                    Console.Error.WriteLine(ex.Code + (ex.Field.Length > 0 ? " (" + ex.Field + ")" : "") + ": " + ex.Message);
                    if (ex.StatusCode == 401 && ex.Code != "invalid_credentials" && ex.Code != "account_inactive") Console.Error.WriteLine("Sign in with 'login', pass --token, or set ARGUEBOT_TOKEN.");
                    return ex.StatusCode == 400 ? 2 : 1;
                }
                catch (Exception ex) when (ex is IOException || ex is System.Net.Http.HttpRequestException || ex is JsonException || ex is ArgumentException || ex is InvalidOperationException || ex is TimeoutException || ex is FormatException)
                { Console.Error.WriteLine(ex.Message); return 1; }
            }
        }
        private static async Task<int> ExecuteAsync(string[] args, CancellationToken token)
        {
            List<string> words = new List<string>(); string? home = null; string? server = null; int? port = null; bool autoStart = true;
            string? bearer = null; string tenantScope = ""; bool allowHttp = false;
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "--home" && index + 1 < args.Length) home = args[++index];
                else if (args[index] == "--token" && index + 1 < args.Length) bearer = args[++index];
                else if (args[index] == "--tenant" && index + 1 < args.Length) tenantScope = args[++index];
                else if (args[index] == "--allow-http") allowHttp = true;
                else if (args[index] == "--server" && index + 1 < args.Length) server = args[++index];
                else if (args[index] == "--port" && index + 1 < args.Length) port = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                else if (args[index] == "--no-start") autoStart = false;
                else words.Add(args[index]);
            }
            if (words.Count == 0 || words.Contains("--help") || words[0] == "help") { Help(); return 0; }
            // 2.1 names for provider secrets; the configuration credentials spelling stays as a deprecated alias.
            if (words[0] == "secrets") { words[0] = "credentials"; words.Insert(0, "configuration"); }
            else if (words[0] == "secret-keys") words[0] = "credential-keys";
            else if (words.Count > 1 && words[0] == "configuration" && words[1] == "credentials") Console.Error.WriteLine("Note: 'configuration credentials' is deprecated; use 'secrets'.");
            if (words[0] == "login" && bearer != null) { words.Add("--token"); words.Add(bearer); bearer = null; }
            if (words[0] == "login" && tenantScope.Length > 0) { words.Add("--tenant"); words.Add(tenantScope); tenantScope = ""; }
            bool web = words[0] == "service" && words.Count == 2 && words[1] == "web";
            if (port.HasValue && !web) throw new ArgumentException("--port is supported by service web for starting a local dashboard service.");
            if (words[0] == "service" && words.Count > 1 && words[1] != "start" && !web) autoStart = false;
            if (words.Count > 1 && words[0] == "service" && words[1] == "stop" && server == null && !File.Exists(Path.Combine(new ArgueBotPaths(home).Home, "runtime", "service.json")))
            { Console.WriteLine("ArgueBot is already stopped."); return 0; }
            try { server = await LocalServiceConnector.ConnectAsync(new ArgueBotPaths(home).Home, server, autoStart, token, port).ConfigureAwait(false); }
            catch (InvalidOperationException exception) when (words.Count > 1 && words[0] == "service" && words[1] == "stop" && exception.Message.StartsWith("The local service is not running.", StringComparison.Ordinal))
            { Console.WriteLine("ArgueBot is already stopped."); return 0; }
            string resolvedHome = new ArgueBotPaths(home).Home;
            if (!allowHttp) allowHttp = ClientSessionStore.Load(resolvedHome) is ClientSession remembered && remembered.AllowInsecureHttp && ClientSessionStore.SameServer(remembered.Server, server);
            using (ArgueBotClient client = LocalServiceConnector.CreateClient(server, resolvedHome, bearer ?? Environment.GetEnvironmentVariable("ARGUEBOT_TOKEN") ?? "", allowHttp))
            {
                client.TenantId = tenantScope;
                if (client.Token.Length == 0 && words[0] != "login") await IdentityCommands.UseRememberedAsync(client, server, resolvedHome, token).ConfigureAwait(false);
                if (IdentityCommands.Names.Contains(words[0])) return await IdentityCommands.RunAsync(client, words, server, resolvedHome, allowHttp, token).ConfigureAwait(false);
                if (web)
                {
                    string dashboard = new Uri(new Uri(server.TrimEnd('/') + "/"), "dashboard").AbsoluteUri;
                    Console.WriteLine("Open ArgueBot in your browser: " + dashboard);
                    Console.WriteLine("The service is running in the background. You can close this terminal.");
                    return 0;
                }
                string command = words[0]; string operation = words.Count > 1 ? words[1] : "list";
                if (command == "resources") return await ResourceCommands.RunAsync(client, words, token).ConfigureAwait(false);
                if (command == "questions") return await QuestionCommands.RunAsync(client, words, token).ConfigureAwait(false);
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
                else if (command == "prompts")
                {
                    switch (operation)
                    {
                        case "list": Print(await client.PromptsAsync(words.Count > 2 ? words[2] : "", token).ConfigureAwait(false)); break;
                        case "show": Require(words, 3); Print(await client.PromptAsync(words[2], token).ConfigureAwait(false)); break;
                        case "parameters": Print(await client.PromptParametersAsync(token).ConfigureAwait(false)); break;
                        case "duplicate": Require(words, 3); Print(await client.DuplicatePromptAsync(words[2], token).ConfigureAwait(false)); break;
                        case "delete": Require(words, 4); Print(await client.DeletePromptAsync(words[2], long.Parse(words[3], System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false)); break;
                        case "add":
                        case "update":
                            {
                                Require(words, operation == "add" ? 3 : 4);
                                PromptTemplate prompt = JsonSerializer.Deserialize<PromptTemplate>(await TextAsync(words, operation == "add" ? 2 : 3, token).ConfigureAwait(false), _Json) ?? throw new ArgumentException("Provide a prompt JSON object.");
                                if (operation == "update") prompt.Id = words[2];
                                Print(await client.SavePromptAsync(prompt, token).ConfigureAwait(false));
                                break;
                            }
                        default: throw new ArgumentException("Use prompts list [PURPOSE], show ID, parameters, add JSON|-|--file PATH, update ID JSON|-|--file PATH, duplicate ID or delete ID REVISION.");
                    }
                }
                else if (command == "conversations")
                {
                    switch (operation)
                    {
                        case "list": Print(await client.ConversationsAsync(words.Count > 2 ? words[2] : "", token: token).ConfigureAwait(false)); break;
                        case "create":
                            {
                                string question = "";
                                int flag = words.IndexOf("--question");
                                if (flag >= 0) { if (flag + 1 >= words.Count) throw new ArgumentException("--question requires the opening question text."); question = words[flag + 1]; words.RemoveRange(flag, 2); }
                                Print(await client.CreateConversationAsync(await TextAsync(words, 2, token).ConfigureAwait(false), token, question).ConfigureAwait(false));
                                break;
                            }
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
                    if (resource != "endpoints" && resource != "models" && resource != "credentials") throw new ArgumentException("Choose endpoints, models or credentials.");
                    string path = "v2.0/api/configuration/" + resource;
                    if (resource == "models" && action == "checks") { Require(words, 4); Print(await client.ModelChecksAsync(words[3], words.Count > 4 ? int.Parse(words[4], System.Globalization.CultureInfo.InvariantCulture) : 1, token).ConfigureAwait(false)); }
                    else if (resource == "models" && action == "check") { Require(words, 5); ModelCheckRequest request = JsonSerializer.Deserialize<ModelCheckRequest>(await TextAsync(words, 4, token).ConfigureAwait(false), _Json) ?? throw new ArgumentException("Supply a model-check JSON request."); Print(await client.CheckModelAsync(words[3], request, token).ConfigureAwait(false)); }
                    else if (action == "list")
                    {
                        if (resource == "endpoints") Print(await client.EndpointsAsync(token).ConfigureAwait(false));
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
                    else if (operation == "clarifications") throw new ArgumentException("debate clarifications was removed in 2.3.0. Use questions list --conversation CONVERSATION --run RUN.");
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
        internal static async Task<string> TextAsync(List<string> words, int index, CancellationToken token)
        {
            if (words.Count > index && words[index] == "--file") { Require(words, index + 2); return await File.ReadAllTextAsync(words[index + 1], token).ConfigureAwait(false); }
            if (words.Count > index && words[index] != "-") return string.Join(" ", words.Skip(index));
            return await Console.In.ReadToEndAsync(token).ConfigureAwait(false);
        }
        internal static void Require(List<string> words, int count) { if (words.Count < count) throw new ArgumentException("Missing command arguments. Use --help."); }
        internal static void Print(object value) { Console.WriteLine(JsonSerializer.Serialize(value, _Json)); }
        private static void Help()
        {
            Console.WriteLine("""
ArgueBot CLI [--home PATH] [--server URL] [--no-start] [--token TOKEN] [--allow-http] COMMAND

Sign-in
  login [--email E] [--tenant ID] [--password-stdin]   Ask for the password without echo and remember
                                             the session for this home and server. The sign-in page
                                             and docs show the default: admin@arguebot / password.
  login --token TOKEN                        Remember a bearer token instead.
  logout                                     End the session and forget it.
  whoami                                     User, tenant and token details.
  me show|password                           Your profile; change your password.
  credentials list|create NAME [--days N]|reveal ID|disable ID|enable ID|delete ID [--user ID]
                                             Bearer tokens. reveal prints one again (audited).
  sessions list|revoke ID [--user ID]

Administration (tenant administrators; --tenant ID lets system administrators work in another tenant)
  users list|show ID|add JSON|-|--file PATH|update ID JSON|-|--file PATH|password ID|delete ID
  audit [--operation TYPE] [--outcome success|failure|denied] [--userId ID] [--pageNumber N]
  tenants list|add NAME|rename ID NAME|enable ID|disable ID|delete ID NAME   (system administrators)

Sharing
  shares list CONVERSATION
  shares create CONVERSATION [--hours N] [--tools]   A read-only public link.
  shares reveal CONVERSATION SHARE_ID       Print an active link again (audited).
  shares revoke CONVERSATION SHARE_ID

Service
  service start|status|restart|stop
  service web [--port N]                     Start or attach, then print the dashboard URL.
  capabilities                               Limits, adapters and supported protocol versions.

Conversations
  conversations list [SEARCH]                Search conversation history (JSON).
  conversations create TITLE|-|--file PATH [--question TEXT]
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
  debate roster|proposals CONVERSATION RUN
  debate edit-proposal CONVERSATION RUN REVISION TEXT|-|--file PATH
                                             Replace the proposal under vote; every seat
                                             votes again. REVISION comes from debate status.
  debate attempts CONVERSATION RUN [PAGE]    Execution and context provenance.
  debate interjections CONVERSATION [PAGE]
  interject CONVERSATION -|--file PATH       Context JSON. deliveryMode is required:
                                             interrupt_now or next_turn. Optional pinned
                                             and recipientId.

Questions (agents asking you during a debate)
  questions list [--status S] [--conversation ID] [--run ID] [--all] [--page N]
                                            Open first. S is open, answered, skipped, expired,
                                            cancelled or resolved. --all lists the whole tenant.
  questions show ID                         One request with its questions, options and answers.
  questions answer ID                       Asks each question on a terminal. Or give answers:
      --choice KEY=OPTION[,OPTION] --other KEY=TEXT --text KEY=TEXT --confirm KEY=yes|no
      --skip-question KEY (optional questions) or --file PATH|- (answer JSON).
  questions skip ID [--note TEXT]           No answer; the agent continues on assumptions.
  questions defer ID                        Answer later: the waiting turn ends now.
  questions watch                           Print question changes as JSON lines.
  questions wait CONVERSATION RUN [--no-input]
                                            Follow a run, answering its questions on a terminal.
                                            Without a terminal or with --no-input, exits 4 while
                                            the run waits for an answer.

Memory
  debate summaries CONVERSATION RUN [PAGE]
  debate summary CONVERSATION RUN SUMMARY_ID [SOURCE_PAGE]
  debate rebuild_summary|exclude_summary CONVERSATION RUN

Prompts
  prompts list [PURPOSE]                    Factory defaults and your prompts; PURPOSE is question,
                                            response, synthesis, ratification or compaction.
  prompts show ID
  prompts parameters                        Placeholders such as {question} and where each applies.
  prompts add JSON|-|--file PATH            {"name","purpose","description","body"}
  prompts update ID JSON|-|--file PATH      Include the current "revision". Factory prompts are read-only.
  prompts duplicate ID                      Editable copy, including of a factory default.
  prompts delete ID REVISION

Resources (named markdown documents agents read with read_resource)
  resources list [SEARCH] [--tag T]         Names, titles, descriptions, sizes and authors.
  resources show NAME|ID [--revision N]     A resource, or one saved revision, with its body.
  resources add --file PATH|- [--name NAME] Markdown; front matter between --- lines may set
                                            name, title, description, tags and locked.
  resources update NAME|ID --revision N --file PATH|-
                                            Replace the text. N is the current revision.
  resources rename NAME|ID NEW_NAME
  resources lock|unlock NAME|ID             Agents can read locked resources but not change them.
  resources delete NAME|ID [--revision N]
  resources history NAME|ID                 Revisions newest first, with user or agent authors.
  resources revert NAME|ID REVISION         Revert to a revision's text, saved as a new revision.
  resources import PATH... [--overwrite]    Up to 50 .md files in one transaction.
  resources export NAME|ID [--output PATH]  Markdown with front matter.

Configuration
  participants list
  participants add|update [ID] JSON|-|--file PATH
  participants delete ID
  configuration endpoints|models list
  configuration endpoints|models add -|--file PATH
  configuration endpoints|models update ID -|--file PATH
  secrets list|add -|--file PATH|update ID -|--file PATH
  secrets reveal ID                          Print a ready secret's decrypted value
  secrets disable ID REVISION                ('configuration credentials ...' still works, deprecated)
  configuration endpoints preview ID         No outbound request or credential resolution.
  configuration endpoints discover ID        Contacts the endpoint; no completion.
  configuration models check ID -|--file PATH   Invokes the model and may incur cost.
  configuration models checks ID [PAGE]
  secret-keys status                         (system administrators)
  secret-keys rotate EXPECTED_KEY_ID
  secret-keys retire EXPECTED_KEY_ID         Remove earlier keys once no secret uses them.
                                             Earlier database copies then lose their secrets.

Every command except service start/status needs a signed-in user: run 'login', pass
--token, or set ARGUEBOT_TOKEN. Remote servers need https:// unless you pass
--allow-http. Prompts and JSON may be supplied on stdin. Secret values are accepted
only from stdin or --file.
Local commands start or attach to the service automatically; --no-start requires
an existing service, and an explicit --server URL attaches without starting one.
Exit codes: 0 success, 1 operation failure, 2 usage, 130 cancelled.
""");
        }
    }
}
