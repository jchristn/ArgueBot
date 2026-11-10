namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;

    /// <summary>Agent resource tools: access modes, visibility, locks, revision conflicts between seats, write limits, provenance, the {resources} placeholder, prompt warnings and each runner transport.</summary>
    internal static class ResourceToolChecks
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        internal static async Task EngineAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-resource-tools-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = Context();
                    await SeedAsync(database, context, token).ConfigureAwait(false);
                    List<string> seats = new List<string>();
                    for (int index = 0; index < 3; index++) seats.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Resource agent " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);
                    PromptTemplate response = await database.Prompts.CreateAsync(context, new PromptTemplate { Name = "Response with resources", Purpose = "response", Body = "You are {agent_name}. Answer: {current_question}\n{resources}\nIf you need billing details, call read_resource with name billing." }, token).ConfigureAwait(false);
                    Require(response.Warnings.Count == 0, "A prompt naming an existing resource must have no warnings.");
                    PromptTemplate warned = await database.Prompts.CreateAsync(context, new PromptTemplate { Name = "Missing reference", Purpose = "response", Body = "Answer {current_question}. Use read_resource(\"no-such-resource\") and read_resource({\"name\": \"billing\"})." }, token).ConfigureAwait(false);
                    Require(warned.Warnings.Count == 1 && warned.Warnings[0].Contains("no-such-resource", StringComparison.Ordinal), "A prompt naming a missing resource must warn once: " + string.Join(" | ", warned.Warnings));
                    Require(PromptCatalog.ResourceReferences("call `read_resource` with name `team/conventions`.").SequenceEqual(new[] { "team/conventions" }), "Prose references must be recognized.");

                    ResourceAgentRunner runner = new ResourceAgentRunner();
                    DebateRun readWrite, off, readOnly;
                    await using (ConversationEngine engine = new ConversationEngine(database, runner))
                    {
                        readWrite = await DebateAsync(engine, database, context, seats, response.Id, new DebatePolicy { ResourceAccess = "read_write", ResourceNames = new List<string> { "Billing", "debate-notes" } }, "rw", token).ConfigureAwait(false);
                        string[] offered = runner.OfferedTools.ToArray();
                        Require(offered.Length >= 2 && offered.All(tools => tools.Contains("read_resource", StringComparison.Ordinal) && tools.Contains("append_resource", StringComparison.Ordinal) && tools.Contains("get_turn", StringComparison.Ordinal)), "read_write debates must offer every resource tool alongside the evidence tools.");
                        string instructions = runner.ResponseInstructions.First();
                        Require(instructions.Contains("Resources you can read with read_resource", StringComparison.Ordinal) && instructions.Contains("- billing: Billing. How invoices work.", StringComparison.Ordinal) && !instructions.Contains("secret-plan", StringComparison.Ordinal), "{resources} must list only the visible resources: " + instructions);
                        Require(runner.Results.Where(result => result.StartsWith("read_resource:", StringComparison.Ordinal)).All(result => result.Contains("Invoices go out on the 1st", StringComparison.Ordinal)), "Agents must read the billing resource.");
                        while (runner.OfferedTools.TryDequeue(out _)) { }
                        while (runner.ResponseInstructions.TryDequeue(out _)) { }
                        off = await DebateAsync(engine, database, context, seats, response.Id, new DebatePolicy { ResourceAccess = "off" }, "off", token).ConfigureAwait(false);
                        Require(runner.OfferedTools.All(tools => !tools.Contains("_resource", StringComparison.Ordinal)), "off must offer no resource tools.");
                        Require(runner.ResponseInstructions.All(text => !text.Contains("Resources you can read", StringComparison.Ordinal)), "{resources} must be empty when access is off.");
                        while (runner.OfferedTools.TryDequeue(out _)) { }
                        readOnly = await DebateAsync(engine, database, context, seats, response.Id, new DebatePolicy(), "read", token).ConfigureAwait(false);
                        Require(readOnly.Policy.ResourceAccess == "read" && runner.OfferedTools.All(tools => tools.Contains("read_resource", StringComparison.Ordinal) && tools.Contains("search_resources", StringComparison.Ordinal) && tools.Contains("list_resources", StringComparison.Ordinal) && !tools.Contains("append_resource", StringComparison.Ordinal) && !tools.Contains("create_resource", StringComparison.Ordinal)), "The default read access must offer only the three read tools.");
                    }

                    // Provenance: each response attempt records the revision it read and the revision its write produced.
                    AttemptPage attempts = await database.Attempts.EnumerateAsync(context, readWrite.ConversationId, readWrite.Id, pageSize: 100, token: token).ConfigureAwait(false);
                    List<GenerationAttempt> answers = attempts.Items.Where(item => item.Purpose == "response").ToList();
                    Require(answers.Count >= 2 && answers.All(item => item.Resources.Any(use => use.Name == "billing" && use.Access == "read" && use.Revision == 1 && use.Tool == "read_resource") && item.Resources.Any(use => use.Name == "debate-notes" && use.Access == "write" && use.Tool == "append_resource" && use.Bytes > 0)), "Attempts must record the resources they read and wrote.");
                    Require(attempts.Items.Where(item => item.Purpose != "response").All(item => item.Resources.Count == 0), "Attempts that used no resource tools must record none.");
                    Resource notes = await database.Resources.ReadByNameAsync(context, "debate-notes", token).ConfigureAwait(false);
                    Require(notes.UpdatedByKind == "agent" && notes.Body.Contains("Note from Resource agent", StringComparison.Ordinal), "Agent appends must be saved and marked as agent-authored.");
                    List<ResourceRevision> history = await database.Resources.RevisionsAsync(context, notes.Id, token).ConfigureAwait(false);
                    Require(history.Where(item => item.AuthorKind == "agent").All(item => item.RunId == readWrite.Id && item.AttemptId.Length > 0 && item.Change == "append"), "Agent revisions must name the run and attempt.");
                    MessagePage transcript = await database.ReadMessagePageAsync(context, readWrite.ConversationId, pageSize: 100, token: token).ConfigureAwait(false);
                    Require(transcript.Items.Any(item => item.Kind == "tool" && item.Content.Contains("read_resource", StringComparison.Ordinal)), "Resource tool activity must appear in the transcript.");
                    AttemptPage offAttempts = await database.Attempts.EnumerateAsync(context, off.ConversationId, off.Id, pageSize: 100, token: token).ConfigureAwait(false);
                    Require(offAttempts.Items.All(item => item.Resources.Count == 0) && off.Policy.ResourceAccess == "off", "off debates must record no resource use.");

                    await DirectAsync(database, context, readWrite, token).ConfigureAwait(false);
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        /// <summary>Direct tool calls on a finished run: visibility, locks, seat conflicts, edits, limits and invalid calls.</summary>
        private static async Task DirectAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, CancellationToken token)
        {
            Require(ResourceTools.Definitions(new DebatePolicy { ResourceAccess = "off" }).Count == 0 && ResourceTools.Definitions(new DebatePolicy()).Count == 3 && ResourceTools.Definitions(new DebatePolicy { ResourceAccess = "read_write" }).Count == 7, "Access modes must offer none, three or seven resource tools.");
            Require(ResourceTools.Definitions(new DebatePolicy { ResourceAccess = "read_write" }).All(definition => JsonDocument.Parse(definition.Schema).RootElement.GetProperty("type").GetString() == "object"), "Every resource tool must have its own object schema.");
            ConversationTools first = new ConversationTools(database, context, run, run.Participants[0].Id, long.MaxValue, "direct-1");
            ConversationTools second = new ConversationTools(database, context, run, run.Participants[1].Id, long.MaxValue, "direct-2");

            // Visibility: named resources plus anything this run created; everything else is not_found.
            JsonElement listed = await OkAsync(first, "list_resources", new { }, token).ConfigureAwait(false);
            Require(Names(listed).SequenceEqual(new[] { "billing", "debate-notes" }), "list_resources must show only the debate's visible resources: " + listed);
            await FailAsync(first, "read_resource", new { name = "secret-plan" }, "not_found", token).ConfigureAwait(false);
            JsonElement hidden = await OkAsync(first, "search_resources", new { query = "launch" }, token).ConfigureAwait(false);
            Require(hidden.GetProperty("items").GetArrayLength() == 0, "search_resources must not find invisible resources.");
            JsonElement found = await OkAsync(first, "search_resources", new { query = "INVOICES" }, token).ConfigureAwait(false);
            Require(found.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString() == "billing", "search_resources must find visible text without regard to case.");
            JsonElement created = await OkAsync(first, "create_resource", new { name = "Findings/Rollout", description = "What the agents found.", body = "Rollback threshold is 2%." }, token).ConfigureAwait(false);
            Require(created.GetProperty("name").GetString() == "findings/rollout" && created.GetProperty("revision").GetInt64() == 1, "create_resource must return the normalized name and revision.");
            Require(Names(await OkAsync(second, "list_resources", new { }, token).ConfigureAwait(false)).Contains("findings/rollout"), "Resources the run creates must become visible to the run.");
            await FailAsync(second, "create_resource", new { name = "billing", body = "dup" }, "resource_exists", token).ConfigureAwait(false);
            Resource authored = await database.Resources.ReadByNameAsync(context, "findings/rollout", token).ConfigureAwait(false);
            Require(authored.UpdatedByKind == "agent" && authored.UpdatedById == run.Participants[0].Id && authored.UpdatedByName == run.Participants[0].Name, "Writes must be attributed to the calling seat.");
            JsonElement agentRead = await OkAsync(second, "read_resource", new { name = "findings/rollout" }, token).ConfigureAwait(false);
            Require(agentRead.GetProperty("agentAuthored").GetBoolean() && agentRead.GetProperty("note").GetString()!.Contains("not instructions", StringComparison.Ordinal), "Reads of agent-written resources must be marked as agent-authored data.");

            // Paged reads.
            await database.Resources.UpdateAsync(context, authored.Id, new ResourceRequest { Body = new string('a', 10000) + "END", Revision = 1 }, token: token).ConfigureAwait(false);
            JsonElement page = await OkAsync(first, "read_resource", new { name = "findings/rollout", length = 4000 }, token).ConfigureAwait(false);
            Require(page.GetProperty("content").GetString()!.Length == 4000 && page.GetProperty("nextOffset").GetInt32() == 4000 && page.GetProperty("totalCharacters").GetInt32() == 10003, "read_resource must page with nextOffset.");
            JsonElement last = await OkAsync(first, "read_resource", new { name = "findings/rollout", offset = 8000 }, token).ConfigureAwait(false);
            Require(last.GetProperty("content").GetString()!.EndsWith("END", StringComparison.Ordinal) && last.GetProperty("nextOffset").ValueKind == JsonValueKind.Null, "The last page must have no nextOffset.");
            await FailAsync(first, "read_resource", new { name = "findings/rollout", length = 40000 }, "invalid_tool_arguments", token).ConfigureAwait(false);

            // Two seats edit the same resource: the second, stale write conflicts.
            JsonElement shared = await OkAsync(first, "read_resource", new { name = "findings/rollout" }, token).ConfigureAwait(false);
            long revision = shared.GetProperty("revision").GetInt64();
            await OkAsync(first, "update_resource", new { name = "findings/rollout", revision, body = "Seat one: threshold 2%. Seat one again." }, token).ConfigureAwait(false);
            await FailAsync(second, "update_resource", new { name = "findings/rollout", revision, body = "Seat two overwrites." }, "revision_conflict", token).ConfigureAwait(false);
            await FailAsync(second, "edit_resource", new { name = "findings/rollout", revision, find = "2%", replace = "3%" }, "revision_conflict", token).ConfigureAwait(false);
            await FailAsync(second, "edit_resource", new { name = "findings/rollout", revision = revision + 1, find = "Seat one", replace = "Seat 1" }, "edit_ambiguous", token).ConfigureAwait(false);
            await FailAsync(second, "edit_resource", new { name = "findings/rollout", revision = revision + 1, find = "absent text", replace = "x" }, "edit_no_match", token).ConfigureAwait(false);
            JsonElement edited = await OkAsync(second, "edit_resource", new { name = "findings/rollout", revision = revision + 1, find = "threshold 2%", replace = "threshold 3%" }, token).ConfigureAwait(false);
            Require(edited.GetProperty("revision").GetInt64() == revision + 2 && (await database.Resources.ReadByNameAsync(context, "findings/rollout", token).ConfigureAwait(false)).Body == "Seat one: threshold 3%. Seat one again.", "edit_resource must replace exactly one occurrence.");

            // Locked resources stay readable and unchangeable.
            Resource billing = await database.Resources.ReadByNameAsync(context, "billing", token).ConfigureAwait(false);
            await database.Resources.UpdateAsync(context, billing.Id, new ResourceRequest { Locked = true, Revision = billing.Revision }, token: token).ConfigureAwait(false);
            JsonElement lockedRead = await OkAsync(first, "read_resource", new { name = "billing" }, token).ConfigureAwait(false);
            Require(lockedRead.GetProperty("locked").GetBoolean(), "Locked resources must stay readable.");
            await FailAsync(first, "append_resource", new { name = "billing", text = "x" }, "resource_locked", token).ConfigureAwait(false);
            await FailAsync(first, "update_resource", new { name = "billing", revision = billing.Revision + 1, body = "x" }, "resource_locked", token).ConfigureAwait(false);
            await FailAsync(first, "edit_resource", new { name = "billing", revision = billing.Revision + 1, find = "1st", replace = "2nd" }, "resource_locked", token).ConfigureAwait(false);

            // Per-turn write limits are counted per attempt; the creation cap per debate.
            run.Policy.MaxResourceWritesPerTurn = 2;
            ConversationTools limited = new ConversationTools(database, context, run, run.Participants[1].Id, long.MaxValue, "direct-limit");
            await OkAsync(limited, "append_resource", new { name = "debate-notes", text = "one" }, token).ConfigureAwait(false);
            await OkAsync(limited, "append_resource", new { name = "debate-notes", text = "two" }, token).ConfigureAwait(false);
            await FailAsync(limited, "append_resource", new { name = "debate-notes", text = "three" }, "resource_write_limit", token).ConfigureAwait(false);
            run.Policy.MaxResourceWritesPerTurn = 8; run.Policy.MaxResourceWriteBytesPerTurn = 10;
            ConversationTools bytes = new ConversationTools(database, context, run, run.Participants[1].Id, long.MaxValue, "direct-bytes");
            await OkAsync(bytes, "append_resource", new { name = "debate-notes", text = "0123456789" }, token).ConfigureAwait(false);
            await FailAsync(bytes, "append_resource", new { name = "debate-notes", text = "x" }, "resource_write_limit", token).ConfigureAwait(false);
            run.Policy.MaxResourceWriteBytesPerTurn = 65536; run.Policy.MaxResourcesCreated = 1;
            ConversationTools capped = new ConversationTools(database, context, run, run.Participants[1].Id, long.MaxValue, "direct-cap");
            await FailAsync(capped, "create_resource", new { name = "second-finding", body = "x" }, "resource_create_limit", token).ConfigureAwait(false);
            run.Policy.MaxResourcesCreated = 50;

            // A read debate cannot call write tools even when it names them; invalid arguments are reported, not thrown.
            run.Policy.ResourceAccess = "read";
            await FailAsync(first, "append_resource", new { name = "debate-notes", text = "x" }, "unknown_tool", token).ConfigureAwait(false);
            run.Policy.ResourceAccess = "off";
            await FailAsync(first, "read_resource", new { name = "billing" }, "unknown_tool", token).ConfigureAwait(false);
            run.Policy.ResourceAccess = "read_write";
            string invalid = await first.ExecuteAsync(new ConversationToolCall { Name = "read_resource", CallId = "invalid-json", ArgumentsJson = "{not json" }, token).ConfigureAwait(false);
            Require(invalid.Contains("invalid_tool_arguments", StringComparison.Ordinal), "Malformed arguments must return invalid_tool_arguments.");
        }

        /// <summary>The MCP bridge (Claude Code and Codex shapes) and the JSON tool envelope carry resource tools to real child processes.</summary>
        internal static async Task TransportAsync(CancellationToken token)
        {
            string fixture = Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!;
            string server = fixture.Replace("Test.Fixtures", "ArgueBot.Server", StringComparison.Ordinal);
            if (!File.Exists(server)) throw new FileNotFoundException("Build the service before testing the native MCP bridge.", server);
            string home = Path.Combine(Path.GetTempPath(), "arguebot-resource-transport-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = Context();
                    await SeedAsync(database, context, token).ConfigureAwait(false);
                    DebateRun run = await FinishedRunAsync(database, context, home, token).ConfigureAwait(false);
                    run.Policy.ResourceAccess = "read_write";
                    foreach (string mode in new[] { "mcp-resources-claude", "mcp-resources-codex", "envelope-resources" })
                    {
                        string provider = mode == "mcp-resources-claude" ? "claude" : mode == "mcp-resources-codex" ? "codex" : "plain";
                        RunnerProfile profile = new RunnerProfile { Kind = "cli", Provider = provider, Executable = "dotnet", Arguments = new List<string> { fixture, mode }, TimeoutSeconds = 30 };
                        profile.RuntimeMcpCommand.AddRange(new[] { "dotnet", server });
                        ConversationTools tools = new ConversationTools(database, context, run, run.Participants[0].Id, long.MaxValue, "transport-" + mode);
                        StringBuilder output = new StringBuilder(); int activities = 0;
                        await new RunnerRouter(true).GenerateWithContextAsync(profile, "Use the resources.", new List<Message>(), ConversationTools.Definitions(run.Policy), tools.ExecuteAsync, false, (text, _) => { output.Append(text); return Task.CompletedTask; }, (_, _) => { activities++; return Task.CompletedTask; }, token).ConfigureAwait(false);
                        string expected = provider == "plain" ? "Envelope resources verified: read billing, note saved." : "MCP resources verified.";
                        Require(output.ToString().Trim() == expected, mode + " did not complete its resource exchange: " + output);
                        Require(activities >= 4, mode + " must report its tool activity.");
                    }
                    Resource notes = await database.Resources.ReadByNameAsync(context, "debate-notes", token).ConfigureAwait(false);
                    Require(notes.Body.Contains("Fixture note via MCP.", StringComparison.Ordinal) && notes.Body.Contains("Fixture note via envelope.", StringComparison.Ordinal) && notes.UpdatedByKind == "agent", "Notes appended over MCP and the envelope must be saved as agent writes.");

                    // With access off, the envelope CLI is offered no resource tools.
                    run.Policy.ResourceAccess = "off";
                    StringBuilder none = new StringBuilder();
                    RunnerProfile plain = new RunnerProfile { Kind = "cli", Provider = "plain", Executable = "dotnet", Arguments = new List<string> { fixture, "envelope-resources" }, TimeoutSeconds = 30 };
                    await new RunnerRouter(true).GenerateWithContextAsync(plain, "Use the resources.", new List<Message>(), ConversationTools.Definitions(run.Policy), new ConversationTools(database, context, run, run.Participants[0].Id, long.MaxValue, "transport-off").ExecuteAsync, false, (text, _) => { none.Append(text); return Task.CompletedTask; }, (_, _) => Task.CompletedTask, token).ConfigureAwait(false);
                    Require(none.ToString().Trim() == "No resource tools offered.", "off must not advertise resource tools over the envelope.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        /// <summary>REST models call resource tools natively through the engine against the committed simulation.</summary>
        internal static async Task RestAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-resource-rest-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext context = Context();
                    await SeedAsync(database, context, token).ConfigureAwait(false);
                    EndpointProfile endpoint = await database.Catalog.SaveEndpointAsync(context, new EndpointProfile { Name = "Resource simulation", Protocol = "ollama", BaseUrl = Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL")!, TimeoutSeconds = 20 }, token).ConfigureAwait(false);
                    ModelProfile model = await database.Catalog.SaveModelAsync(context, new ModelProfile { Name = "Resource model", EndpointId = endpoint.Id, Model = "multi-agent-resources", SupportsTools = true }, token).ConfigureAwait(false);
                    List<string> seats = new List<string>();
                    for (int index = 0; index < 3; index++) seats.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "REST resource agent " + index, ModelProfileId = model.Id }, token).ConfigureAwait(false)).Id);
                    Conversation conversation = await database.CreateConversationAsync(context, "REST resources", token).ConfigureAwait(false);
                    using (ArgueBot.Core.Services.CredentialVault vault = await ArgueBot.Core.Services.CredentialVault.OpenAsync(database, Path.Combine(home, "secrets"), context, token).ConfigureAwait(false))
                    await using (ConversationEngine engine = new ConversationEngine(database, new CredentialRunner(false, vault, database, context, Path.Combine(home, "runtime"))))
                    {
                        DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats, MaxRounds = 1, InterventionSeconds = 0, Policy = new DebatePolicy { ResourceAccess = "read_write", Intake = "off" }, IdempotencyKey = "rest-resources" }, token).ConfigureAwait(false);
                        run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
                        Require(run.State == "completed", "The REST resource debate must finish: " + run.State + "/" + run.Outcome);
                        AttemptPage attempts = await database.Attempts.EnumerateAsync(context, conversation.Id, run.Id, pageSize: 100, token: token).ConfigureAwait(false);
                        List<GenerationAttempt> answers = attempts.Items.Where(item => item.Purpose == "response").ToList();
                        Require(answers.Count == 3 && answers.All(item => item.Resources.Any(use => use.Name == "billing" && use.Access == "read") && item.Resources.Any(use => use.Name == "debate-notes" && use.Access == "write")), "Native REST tool calls must read and write resources with provenance.");
                        MessagePage transcript = await database.ReadMessagePageAsync(context, conversation.Id, pageSize: 100, token: token).ConfigureAwait(false);
                        Require(transcript.Items.Count(item => item.Kind != "tool" && item.Content.Contains("Billing context read: invoices go out on the 1st", StringComparison.Ordinal)) == 3, "Each REST agent must answer with the resource it read.");
                    }
                    Resource notes = await database.Resources.ReadByNameAsync(context, "debate-notes", token).ConfigureAwait(false);
                    Require(notes.UpdatedByKind == "agent" && notes.Body.Contains("REST note", StringComparison.Ordinal), "REST appends must be saved as agent writes.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        private static async Task SeedAsync(DatabaseDriverBase database, RequestContext context, CancellationToken token)
        {
            await database.Resources.CreateAsync(context, new ResourceRequest { Name = "billing", Title = "Billing", Description = "How invoices work.", Body = "# Billing\nInvoices go out on the 1st. 議論" }, token: token).ConfigureAwait(false);
            await database.Resources.CreateAsync(context, new ResourceRequest { Name = "debate-notes", Title = "Debate notes", Description = "Running notes agents keep.", Body = "" }, token: token).ConfigureAwait(false);
            await database.Resources.CreateAsync(context, new ResourceRequest { Name = "secret-plan", Title = "Secret plan", Description = "Not for this debate.", Tags = new List<string> { "private" }, Body = "The launch date is confidential." }, token: token).ConfigureAwait(false);
        }

        private static async Task<DebateRun> DebateAsync(ConversationEngine engine, DatabaseDriverBase database, RequestContext context, List<string> seats, string responsePrompt, DebatePolicy policy, string key, CancellationToken token)
        {
            policy.Intake = "off";
            Conversation conversation = await database.CreateConversationAsync(context, "Resource tools " + key, token).ConfigureAwait(false);
            CreateRunRequest request = new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "When do invoices go out?", MaxRounds = 1, InterventionSeconds = 0, Policy = policy, PromptIds = new Dictionary<string, string> { ["response"] = responsePrompt }, IdempotencyKey = "resource-" + key };
            DebateRun run = await engine.StartAsync(context, conversation.Id, request, token).ConfigureAwait(false);
            run = await WaitAsync(database, context, run, token).ConfigureAwait(false);
            Require(run.State == "completed", "The " + key + " debate must finish: " + run.State + "/" + run.Outcome);
            return run;
        }

        private static async Task<DebateRun> FinishedRunAsync(DatabaseDriverBase database, RequestContext context, string home, CancellationToken token)
        {
            List<string> seats = new List<string>();
            for (int index = 0; index < 3; index++) seats.Add((await database.Profiles.CreateAsync(context, new RunnerProfile { Name = "Transport agent " + index, Executable = "fictional", WorkingDirectory = home }, token).ConfigureAwait(false)).Id);
            Conversation conversation = await database.CreateConversationAsync(context, "Resource transport", token).ConfigureAwait(false);
            await using (ConversationEngine engine = new ConversationEngine(database, new MultiAgentFixtureRunner()))
            {
                DebateRun run = await engine.StartAsync(context, conversation.Id, new CreateRunRequest { ProtocolVersion = 2, OrchestratorId = seats[0], ParticipantIds = seats.Skip(1).ToList(), Question = "Transport?", MaxRounds = 1, InterventionSeconds = 0, IdempotencyKey = "transport" }, token).ConfigureAwait(false);
                return await WaitAsync(database, context, run, token).ConfigureAwait(false);
            }
        }

        private static async Task<DebateRun> WaitAsync(DatabaseDriverBase database, RequestContext context, DebateRun run, CancellationToken token)
        {
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                do
                {
                    await Task.Delay(10, deadline.Token).ConfigureAwait(false);
                    run = await database.Runs.ReadAsync(context, run.ConversationId, run.Id, deadline.Token).ConfigureAwait(false);
                } while (run.State != "completed" && run.State != "paused" && run.State != "failed" && run.State != "cancelled");
                return run;
            }
        }

        private static async Task<JsonElement> OkAsync(ConversationTools tools, string name, object arguments, CancellationToken token)
        {
            string json = await tools.ExecuteAsync(new ConversationToolCall { Name = name, CallId = name + ":" + Guid.NewGuid().ToString("N"), ArgumentsJson = JsonSerializer.Serialize(arguments, Json) }, token).ConfigureAwait(false);
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                Require(document.RootElement.GetProperty("success").GetBoolean(), name + " failed: " + json);
                return document.RootElement.GetProperty("result").Clone();
            }
        }

        private static async Task FailAsync(ConversationTools tools, string name, object arguments, string code, CancellationToken token)
        {
            string json = await tools.ExecuteAsync(new ConversationToolCall { Name = name, CallId = name + ":" + Guid.NewGuid().ToString("N"), ArgumentsJson = JsonSerializer.Serialize(arguments, Json) }, token).ConfigureAwait(false);
            using (JsonDocument document = JsonDocument.Parse(json))
                Require(!document.RootElement.GetProperty("success").GetBoolean() && document.RootElement.GetProperty("code").GetString() == code, name + " must fail with " + code + ": " + json);
        }

        private static List<string> Names(JsonElement list) { return list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToList(); }

        private static RequestContext Context() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, Role = "admin" }; }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
