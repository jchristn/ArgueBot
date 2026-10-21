namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using ArgueBot.Runners.Processes;
    using Touchstone.Core;

    /// <summary>Actual PolyPrompt HTTP transport against a local Postman simulation.</summary>
    public static class RunnerSuites
    {
        /// <summary>Simulation descriptors; missing local simulation is an explicit skip.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                bool missing = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL"));
                bool fixtureMissing = !File.Exists(Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE"));
                return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Runners", "PolyPrompt transport", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Runners", "CodexMessages", "Codex progress and final messages stream independently by item ID without duplicates", CodexMessagesAsync, skip: fixtureMissing, skipReason: fixtureMissing ? "Set ARGUEBOT_CLI_FIXTURE to the built Test.Fixtures.dll." : null),
                    new TestCaseDescriptor("Runners", "MuxCapture", "Mux streams UTF-8 answers, excludes stderr and passes literal stdin/arguments", MuxCaptureAsync, skip: fixtureMissing, skipReason: fixtureMissing ? "Set ARGUEBOT_CLI_FIXTURE to the built Test.Fixtures.dll." : null),
                    new TestCaseDescriptor("Runners", "CliCaptureFailure", "Malformed output and capture overflow terminate a live process promptly", CaptureFailureAsync, skip: fixtureMissing, skipReason: fixtureMissing ? "Set ARGUEBOT_CLI_FIXTURE to the built Test.Fixtures.dll." : null),
                    new TestCaseDescriptor("Runners", "RestTools", "Four REST providers execute real local file/process tools and continue with results", LocalToolsAsync, skip: missing || fixtureMissing, skipReason: missing || fixtureMissing ? "Set simulation URL and CLI fixture." : null),
                    new TestCaseDescriptor("Runners", "RestToolBoundaries", "Tool errors recover, incomplete streams execute nothing, execution policy and loop limits hold", ToolBoundariesAsync, skip: missing || fixtureMissing, skipReason: missing || fixtureMissing ? "Set simulation URL and CLI fixture." : null),
                    new TestCaseDescriptor("Runners", "RestToolCancellation", "Cancellation stops the actual local process tree and reports interruption", ToolCancellationAsync, skip: missing || fixtureMissing, skipReason: missing || fixtureMissing ? "Set simulation URL and CLI fixture." : null),
                    new TestCaseDescriptor("Runners", "RestToolHistory", "Service engine persists tool checkpoints and results separately from final answers", ToolHistoryAsync, skip: missing || fixtureMissing, skipReason: missing || fixtureMissing ? "Set simulation URL and CLI fixture." : null),
                    new TestCaseDescriptor("Runners", "OllamaStream", "Real PolyPrompt receives NDJSON answer", StreamAsync, skip: missing, skipReason: missing ? "Set ARGUEBOT_SIMULATION_URL to the local Postman runner simulation." : null),
                    new TestCaseDescriptor("Runners", "OllamaRateLimit", "Rate limit becomes safe failure without replay", RateLimitAsync, skip: missing, skipReason: missing ? "Local Postman simulation is not configured." : null),
                    new TestCaseDescriptor("Runners", "OllamaBroken", "Truncated upstream stream does not report success", BrokenAsync, skip: missing, skipReason: missing ? "Local Postman simulation is not configured." : null)
                }) };
            }
        }
        private static List<Message> ToolInput()
        { return new List<Message> { new Message { Speaker = "user", Content = JsonSerializer.Serialize(new { fixtureExecutable = Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE") }) } }; }

        private static async Task LocalToolsAsync(CancellationToken token)
        {
            foreach (string provider in new[] { "ollama", "openai", "anthropic", "gemini" })
            {
                string home = Path.Combine(Path.GetTempPath(), "arguebot-tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
                try
                {
                    RunnerProfile profile = Profile("local-tools"); profile.Provider = provider; profile.WorkingDirectory = home; profile.TimeoutSeconds = 20;
                    StringBuilder text = new StringBuilder(); List<string> states = new List<string>();
                    await new RunnerRouter(true).GenerateWithToolsAsync(profile, "Exercise local tooling.", ToolInput(), (delta, _) => { text.Append(delta); return Task.CompletedTask; }, (activity, _) => { states.Add(activity.Name + ":" + activity.State); return Task.CompletedTask; }, token).ConfigureAwait(false);
                    if (text.ToString().Trim() != "Local tools verified." || states.Count != 8 || states.Count(state => state.EndsWith(":completed", StringComparison.Ordinal)) != 4 || await File.ReadAllTextAsync(Path.Combine(home, "tool fixture.txt"), token).ConfigureAwait(false) != "Local tool evidence 議論") throw new InvalidDataException(provider + " local tooling failed: " + text + " / " + string.Join(",", states));
                }
                finally { Directory.Delete(home, true); }
            }
        }

        private static async Task ToolBoundariesAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-tools-boundaries-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            try
            {
                RunnerProfile profile = Profile("tool-failure"); profile.WorkingDirectory = home;
                StringBuilder text = new StringBuilder(); List<string> states = new List<string>();
                await new PolyPromptRunner().GenerateWithToolsAsync(profile, "Exercise an error.", new List<Message>(), (delta, _) => { text.Append(delta); return Task.CompletedTask; }, (activity, _) => { states.Add(activity.State); return Task.CompletedTask; }, token).ConfigureAwait(false);
                if (text.ToString().Trim() != "Tool error recovered." || !states.SequenceEqual(new[] { "started", "failed" })) throw new InvalidDataException("Tool failure did not reach the model as a recoverable result.");
                profile.Model = "tool-broken"; bool broken = false; int calls = 0;
                try { await new PolyPromptRunner().GenerateWithToolsAsync(profile, "Incomplete tool response.", new List<Message>(), (_, _) => Task.CompletedTask, (_, _) => { calls++; return Task.CompletedTask; }, token).ConfigureAwait(false); }
                catch (IOException) { broken = true; }
                if (!broken || calls != 0 || File.Exists(Path.Combine(home, "should-not-exist.txt"))) throw new InvalidDataException("An incomplete model response executed a local tool.");
                profile.Model = "tool-loop"; bool limited = false;
                try { await new PolyPromptRunner().GenerateAsync(profile, "Loop fixture.", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "tool_limit") { limited = true; }
                if (!limited) throw new InvalidDataException("Tool continuation was unbounded.");
                profile.Model = "tool-many"; calls = 0; limited = false;
                try { await new PolyPromptRunner().GenerateWithToolsAsync(profile, "Too many calls.", new List<Message>(), (_, _) => Task.CompletedTask, (_, _) => { calls++; return Task.CompletedTask; }, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "tool_limit") { limited = true; }
                if (!limited || calls != 0 || File.Exists(Path.Combine(home, "should-not-exist.txt"))) throw new InvalidDataException("An oversized tool batch executed operations before validation.");
                profile.Model = "tool-limits"; states.Clear(); text.Clear();
                await File.WriteAllTextAsync(Path.Combine(home, "large.txt"), new string('x', 65537), token).ConfigureAwait(false);
                await new PolyPromptRunner().GenerateWithToolsAsync(profile, "Bounded tools.", ToolInput(), (delta, _) => { text.Append(delta); return Task.CompletedTask; }, (activity, _) => { states.Add(activity.State); return Task.CompletedTask; }, token).ConfigureAwait(false);
                if (states.Count(state => state == "failed") != 2 || text.ToString() != "Tool limits verified.") throw new InvalidDataException("Oversized file/process capture was not returned as bounded tool failure.");
                profile.Model = "tool-duplicate"; profile.Provider = "openai"; bool duplicate = false;
                try { await new PolyPromptRunner().GenerateAsync(profile, "Repeated call.", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "duplicate_tool_call") { duplicate = true; }
                if (!duplicate || await File.ReadAllTextAsync(Path.Combine(home, "duplicate.txt"), token).ConfigureAwait(false) != "first") throw new InvalidDataException("Duplicate provider call executed twice.");
                profile.Provider = "ollama";
                profile.Model = "tools-disabled"; text.Clear();
                await new RunnerRouter(false).GenerateWithToolsAsync(profile, "Policy fixture.", new List<Message>(), (delta, _) => { text.Append(delta); return Task.CompletedTask; }, (_, _) => throw new InvalidDataException("Disabled tools executed."), token).ConfigureAwait(false);
                if (text.ToString() != "Tools disabled.") throw new InvalidDataException("Execution-disabled service still advertised local tools.");
            }
            finally { Directory.Delete(home, true); }
        }

        private static async Task ToolCancellationAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-tools-cancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            try
            {
                RunnerProfile profile = Profile("tool-cancel"); profile.WorkingDirectory = home; profile.TimeoutSeconds = 20;
                using (CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    List<string> states = new List<string>();
                    Task run = new PolyPromptRunner().GenerateWithToolsAsync(profile, "Cancellation fixture.", ToolInput(), (_, _) => Task.CompletedTask, (activity, _) => { states.Add(activity.State); return Task.CompletedTask; }, stop.Token);
                    string marker = Path.Combine(home, "tool.pid");
                    using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        while (!File.Exists(marker)) { if (run.IsCompleted) await run.ConfigureAwait(false); await Task.Delay(20, deadline.Token).ConfigureAwait(false); }
                    }
                    int pid = int.Parse(await File.ReadAllTextAsync(marker, token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                    stop.Cancel(); bool cancelled = false;
                    try { await run.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { cancelled = true; }
                    bool alive = false;
                    int childPid = int.Parse(await File.ReadAllTextAsync(marker + ".child", token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
                    foreach (int processId in new[] { pid, childPid })
                    {
                        try { using (Process process = Process.GetProcessById(processId)) { await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false); alive |= !process.HasExited; } }
                        catch (ArgumentException) { }
                    }
                    if (!cancelled || alive || !states.Contains("interrupted")) throw new InvalidDataException("Cancellation left a tool process running or lost its interrupted checkpoint.");
                }
            }
            finally { Directory.Delete(home, true); }
        }

        private static async Task ToolHistoryAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-tools-history-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(home);
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext owner = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    RunnerProfile profile = Profile("local-tools"); profile.WorkingDirectory = home; profile.TimeoutSeconds = 20;
                    profile = await database.Profiles.CreateAsync(owner, profile, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(owner, "REST local tool history", token).ConfigureAwait(false);
                    await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Speaker = "user", Content = ToolInput()[0].Content }, token: token).ConfigureAwait(false);
                    await using (ConversationEngine engine = new ConversationEngine(database, new RunnerRouter(true)))
                    {
                        DebateRun run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Use local tools and verify the fixture.", ParticipantIds = new List<string> { profile.Id }, MaxRounds = 1, IdempotencyKey = "local-tools" }, token).ConfigureAwait(false);
                        using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            deadline.CancelAfter(TimeSpan.FromSeconds(20));
                            do { await Task.Delay(20, deadline.Token).ConfigureAwait(false); run = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, deadline.Token).ConfigureAwait(false); } while (run.State != "completed" && run.State != "paused");
                        }
                        List<Message> messages = await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false);
                        if (run.State != "completed" || messages.Count(message => message.Kind == "tool") != 8 || !messages.Any(message => message.Kind == "final" && message.Content.Trim() == "Local tools verified.")) throw new InvalidDataException("Tool history or final answer was not durably completed: " + run.State + " / " + run.Outcome);
                        List<DurableEvent> events = await database.ReadEventsAsync(owner, conversation.Id, token: token).ConfigureAwait(false);
                        foreach (Message activity in messages.Where(message => message.Kind == "tool")) if (!events.Any(change => change.EntityId == activity.Id && change.Kind == "message")) throw new InvalidDataException("Tool activity was missing its replay event.");
                    }
                }
            }
            finally { Directory.Delete(home, true); }
        }

        private static async Task CodexMessagesAsync(CancellationToken token)
        {
            RunnerProfile profile = new RunnerProfile { Name = "Codex fixture", Kind = "cli", Provider = "codex", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, "codex-messages" }, TimeoutSeconds = 10 };
            StringBuilder output = new StringBuilder();
            await new CliRunner().GenerateAsync(profile, "Fixture input", new List<Message>(), (text, _) => { output.Append(text); return Task.CompletedTask; }, token).ConfigureAwait(false);
            if (output.ToString() != "Checking the explanation.\n\nFinal answer 議論.") throw new InvalidDataException("Codex progress/final items were duplicated, dropped or treated as conflicting snapshots.");
            profile.Arguments[1] = "codex-conflict";
            bool rejected = false;
            try { await new CliRunner().GenerateAsync(profile, "Fixture input", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("A conflicting replacement for the same Codex item was accepted.");
        }
        private static async Task MuxCaptureAsync(CancellationToken token)
        {
            RunnerProfile profile = new RunnerProfile { Name = "Mux fixture", Kind = "cli", Provider = "mux", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, "mux", "--endpoint", "fixture endpoint" }, TimeoutSeconds = 10 };
            StringBuilder output = new StringBuilder();
            await new CliRunner().GenerateAsync(profile, "Literal $(echo unsafe) `prompt` 議論", new List<Message>(), (text, _) => { output.Append(text); return Task.CompletedTask; }, token).ConfigureAwait(false);
            if (output.ToString() != "Mux answer 議論.\n") throw new InvalidDataException("Mux capture mixed diagnostics into the answer or corrupted UTF-8.");
            profile.Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, "exit" };
            bool failed = false;
            try { await new CliRunner().GenerateAsync(profile, "input", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
            catch (DomainException ex) when (ex.Code == "agent_exit") { failed = true; }
            if (!failed) throw new InvalidDataException("Mux nonzero exit was reported as success.");
        }
        private static async Task CaptureFailureAsync(CancellationToken token)
        {
            foreach (string mode in new string[] { "malformed-hang", "stderr-hang" })
            {
                RunnerProfile profile = new RunnerProfile { Name = "capture-fixture", Kind = "cli", Provider = mode == "malformed-hang" ? "claude" : "plain", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, mode }, MaxOutputBytes = 1024, TimeoutSeconds = 30 };
                Stopwatch watch = Stopwatch.StartNew();
                bool failed = false;
                try { await new CliRunner().GenerateAsync(profile, "Fixture input", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException) { failed = true; }
                if (!failed || watch.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidDataException("A capture failure did not terminate the live process promptly: " + mode);
            }
        }
        private static RunnerProfile Profile(string model) { return new RunnerProfile { Name = "simulated", Kind = "rest", Provider = "ollama", Endpoint = Environment.GetEnvironmentVariable("ARGUEBOT_SIMULATION_URL")!, Model = model, TimeoutSeconds = 5 }; }
        private static async Task StreamAsync(CancellationToken token)
        {
            StringBuilder output = new StringBuilder();
            await new PolyPromptRunner().GenerateAsync(Profile("fixture"), "Debate a fixture topic.", new List<Message>(), (text, _) => { output.Append(text); return Task.CompletedTask; }, token).ConfigureAwait(false);
            if (output.ToString() != "Fixture answer.") throw new InvalidDataException("Unexpected streamed content: " + output);
        }
        private static async Task RateLimitAsync(CancellationToken token)
        {
            bool failed = false;
            try { await new PolyPromptRunner().GenerateAsync(Profile("rate-limit"), "Debate.", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
            catch (DomainException ex) when (ex.Code == "provider_error") { failed = true; }
            if (!failed) throw new InvalidDataException("Rate limit was not surfaced.");
        }
        private static async Task BrokenAsync(CancellationToken token)
        {
            bool failed = false;
            try { await new PolyPromptRunner().GenerateAsync(Profile("broken-stream"), "Debate.", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
            catch (IOException) { failed = true; }
            if (!failed) throw new InvalidDataException("Partial stream was treated as complete.");
        }
    }
}
