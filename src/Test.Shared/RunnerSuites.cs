namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using ArgueBot.Runners.Processes;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
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
                    new TestCaseDescriptor("Runners", "ClaudeToolReplay", "Claude tool-result events and multi-message streams produce a complete durable answer without leaking tool payloads", ClaudeToolReplayAsync, skip: fixtureMissing, skipReason: fixtureMissing ? "Set ARGUEBOT_CLI_FIXTURE to the built Test.Fixtures.dll." : null),
                    new TestCaseDescriptor("Runners", "CliDiagnostics", "Runner failures preserve bounded raw records, stderr, parser detail and durable attempt correlation", DiagnosticsAsync, skip: fixtureMissing, skipReason: fixtureMissing ? "Set ARGUEBOT_CLI_FIXTURE to the built Test.Fixtures.dll." : null),
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
        private static async Task ClaudeToolReplayAsync(CancellationToken token)
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "claude-tool-stream.jsonl");
            RunnerProfile profile = new RunnerProfile { Name = "Claude replay", Kind = "cli", Provider = "claude", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, "claude-tool-replay", fixture }, TimeoutSeconds = 10 };
            string expected = "Checking the fixture. 議論\n\nFinal fixture answer. 議論";
            StringBuilder text = new StringBuilder();
            try { await new RunnerRouter(true).GenerateAsync(profile, "Fictional tool replay", new List<Message>(), (delta, _) => { text.Append(delta); return Task.CompletedTask; }, token).ConfigureAwait(false); }
            catch (InvalidDataException exception) { throw new InvalidDataException("Claude replay rejected a valid native event: " + exception.InnerException?.Message, exception); }
            if (text.ToString() != expected) throw new InvalidDataException("Claude replay lost or duplicated answer text, or exposed tool data: " + text);
            profile.Arguments[2] = Path.Combine(AppContext.BaseDirectory, "fixtures", "claude-tools-before-text.jsonl");
            expected = "Final fixture answer. 議論";
            text.Clear();
            await new RunnerRouter(true).GenerateAsync(profile, "Tools before text", new List<Message>(), (delta, _) => { text.Append(delta); return Task.CompletedTask; }, token).ConfigureAwait(false);
            if (text.ToString() != expected) throw new InvalidDataException("Tool-only opening prevented the final answer.");
            foreach (string mode in new[] { "claude-empty", "claude-error-result", "claude-truncated", "claude-conflict", "claude-result-conflict", "claude-snapshots" })
            {
                RunnerProfile boundary = new RunnerProfile { Name = "Claude boundary", Kind = "cli", Provider = "claude", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, mode }, TimeoutSeconds = 10 };
                Exception? failure = null; StringBuilder answer = new StringBuilder();
                try { await new CliRunner().GenerateAsync(boundary, "Fictional Claude boundary", new List<Message>(), (delta, _) => { answer.Append(delta); return Task.CompletedTask; }, token).ConfigureAwait(false); }
                catch (Exception exception) { failure = exception; }
                if (mode == "claude-snapshots")
                {
                    if (failure != null || answer.ToString() != "First answer.\n\nFinal answer.") throw new InvalidDataException("Non-streamed Claude snapshots were not accepted without duplication.");
                    continue;
                }
                string code = mode == "claude-empty" ? "empty_answer" : mode == "claude-error-result" ? "agent_result_error" : "incomplete_answer";
                if (mode.EndsWith("conflict", StringComparison.Ordinal))
                {
                    if (failure is not InvalidDataException) throw new InvalidDataException("Conflicting Claude output was accepted: " + mode);
                }
                else if (failure is not DomainException domain || domain.Code != code) throw new InvalidDataException("Claude boundary was incorrectly accepted or misclassified: " + mode);
                if (failure!.Data[RunnerFailureDiagnostics.ExceptionDataKey] is not RunnerFailureDiagnostics diagnostics || diagnostics.StandardOutput.Length == 0) throw new InvalidDataException("Claude boundary lost its retained output.");
            }
            string home = Path.Combine(Path.GetTempPath(), "arguebot-claude-replay-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext owner = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    profile = await database.Profiles.CreateAsync(owner, profile, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(owner, "Claude tool replay", token).ConfigureAwait(false);
                    await using (ConversationEngine engine = new ConversationEngine(database, new RunnerRouter(true), diagnosticDirectory: Path.Combine(home, "logs", "runner-failures")))
                    {
                        DebateRun run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Fictional tool replay", ParticipantIds = new List<string> { profile.Id }, MaxRounds = 1, IdempotencyKey = "claude-tool-replay" }, token).ConfigureAwait(false);
                        using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            deadline.CancelAfter(TimeSpan.FromSeconds(10));
                            do { await Task.Delay(20, deadline.Token).ConfigureAwait(false); run = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, deadline.Token).ConfigureAwait(false); } while (run.State != "completed" && run.State != "paused");
                        }
                        AttemptPage attempts = await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                        List<Message> messages = await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false);
                        if (run.State != "completed" || attempts.Items.Single().State != "completed" || messages.Single(message => message.Id == attempts.Items.Single().MessageId).Content != expected) throw new InvalidDataException("Claude tools did not reach a durable completed answer: " + run.Outcome);
                    }
                }
            }
            finally { Directory.Delete(home, true); }
        }
        private static async Task DiagnosticsAsync(CancellationToken token)
        {
            foreach (string mode in new[] { "malformed-hang", "diagnostic-schema", "codex-conflict", "diagnostic-exit", "diagnostic-large", "diagnostic-timeout", "stderr-hang", "stdout-hang" })
            {
                RunnerProfile profile = new RunnerProfile { Name = "Diagnostic fixture", Kind = "cli", Provider = mode == "codex-conflict" ? "codex" : "claude", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, mode }, MaxOutputBytes = mode.EndsWith("-hang", StringComparison.Ordinal) ? 1024 : 65536, TimeoutSeconds = mode == "diagnostic-timeout" ? 1 : 10 };
                Exception? failure = null;
                try { await new CliRunner().GenerateAsync(profile, "Fictional prompt", new List<Message>(), (_, _) => Task.CompletedTask, token).ConfigureAwait(false); }
                catch (Exception exception) { failure = exception; }
                if (failure?.Data[RunnerFailureDiagnostics.ExceptionDataKey] is not RunnerFailureDiagnostics capture) throw new InvalidDataException("Runner failure lost process evidence: " + mode);
                if (capture.Provider != profile.Provider || capture.Detail.Length == 0 || capture.StandardOutput.Length > RunnerFailureDiagnostics.CaptureCharacters || capture.StandardError.Length > RunnerFailureDiagnostics.CaptureCharacters || capture.TriggeringRecord.Length > RunnerFailureDiagnostics.CaptureCharacters) throw new InvalidDataException("Missing or unbounded failure evidence: " + mode);
                if (mode == "malformed-hang" && (capture.TriggeringRecord != "{invalid" || !capture.Detail.Contains("malformed JSONL", StringComparison.Ordinal))) throw new InvalidDataException("Malformed record was discarded.");
                if (mode == "diagnostic-schema" && (!capture.Detail.Contains("$.message.content", StringComparison.Ordinal) || !capture.TriggeringRecord.Contains("unexpected string", StringComparison.Ordinal))) throw new InvalidDataException("Schema mismatch lost its exact record or parser path.");
                if (mode == "codex-conflict" && (!capture.Detail.Contains("conflicts", StringComparison.Ordinal) || !capture.TriggeringRecord.Contains("Conflicting replacement.", StringComparison.Ordinal))) throw new InvalidDataException("Conflicting snapshot evidence was lost.");
                if (mode.StartsWith("diagnostic-", StringComparison.Ordinal) && !capture.StandardError.Contains("Fictional harness diagnostic 議論", StringComparison.Ordinal)) throw new InvalidDataException("Stderr was discarded or corrupted.");
                if (mode == "diagnostic-exit" && (capture.ExitCode != 17 || failure is not DomainException domain || domain.Code != "agent_exit")) throw new InvalidDataException("Nonzero process exit diagnostics were lost.");
                if (mode == "diagnostic-large" && (!capture.Truncated || !capture.TriggeringRecord.EndsWith("TRIGGER-END", StringComparison.Ordinal))) throw new InvalidDataException("Oversized triggering record did not retain a bounded tail.");
                if (mode == "stdout-hang" && (capture.StandardOutput.Length == 0 || !capture.Detail.Contains("stdout exceeded", StringComparison.Ordinal))) throw new InvalidDataException("Stdout overflow discarded its evidence.");
                if (mode == "diagnostic-timeout" && failure is not OperationCanceledException) throw new InvalidDataException("Timeout lost cancellation semantics.");
            }
            string home = Path.Combine(Path.GetTempPath(), "arguebot-diagnostics-" + Guid.NewGuid().ToString("N"));
            try
            {
                string directory = Path.Combine(home, "logs", "runner-failures");
                Directory.CreateDirectory(directory);
                for (int index = 0; index < 100; index++)
                {
                    string old = Path.Combine(directory, "att_fixture_" + index + ".json");
                    await File.WriteAllTextAsync(old, "{}", token).ConfigureAwait(false);
                    File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2).AddSeconds(index));
                }
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext owner = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, Role = "admin" };
                    RunnerProfile profile = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Diagnostic fixture", Kind = "cli", Provider = "claude", Executable = "dotnet", Arguments = new List<string> { Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!, "diagnostic-schema" } }, token).ConfigureAwait(false);
                    Conversation conversation = await database.CreateConversationAsync(owner, "Failure evidence fixture", token).ConfigureAwait(false);
                    DebateRun run;
                    await using (ConversationEngine engine = new ConversationEngine(database, new CliRunner(), diagnosticDirectory: directory))
                    {
                        run = await engine.StartAsync(owner, conversation.Id, new CreateRunRequest { Mode = "followup", Question = "Fictional diagnostic input", ParticipantIds = new List<string> { profile.Id }, MaxRounds = 1, IdempotencyKey = "diagnostic-fixture" }, token).ConfigureAwait(false);
                        using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            deadline.CancelAfter(TimeSpan.FromSeconds(10));
                            do { await Task.Delay(20, deadline.Token).ConfigureAwait(false); run = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, deadline.Token).ConfigureAwait(false); } while (run.State != "paused");
                        }
                    }
                    AttemptPage attempts = await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false);
                    GenerationAttempt attempt = attempts.Items.Single();
                    string path = Path.Combine(directory, attempt.Id + ".json");
                    if (Directory.GetFiles(directory, "att_*.json").Length != 100 || File.Exists(Path.Combine(directory, "att_fixture_0.json"))) throw new InvalidDataException("Diagnostic retention failed to prune the oldest capture.");
                    using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)))
                    {
                        JsonElement root = document.RootElement;
                        if (attempt.State != "failed" || run.Outcome != "InvalidDataException" || root.GetProperty("Id").GetString() != attempt.Id || root.GetProperty("MessageId").GetString() != attempt.MessageId || !root.GetProperty("Diagnostics").GetProperty("TriggeringRecord").GetString()!.Contains("unexpected string", StringComparison.Ordinal)) throw new InvalidDataException("Durable evidence lost attempt or failed-turn correlation.");
                    }
                    if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0) throw new InvalidDataException("Diagnostic evidence is not owner-only.");
                    // A full or unwritable diagnostic destination must not prevent the attempt checkpoint.
                    Conversation blockedConversation = await database.CreateConversationAsync(owner, "Unavailable diagnostic destination", token).ConfigureAwait(false);
                    await using (ConversationEngine blocked = new ConversationEngine(database, new CliRunner(), diagnosticDirectory: path))
                    {
                        DebateRun blockedRun = await blocked.StartAsync(owner, blockedConversation.Id, new CreateRunRequest { Mode = "followup", Question = "Fictional blocked capture", ParticipantIds = new List<string> { profile.Id }, MaxRounds = 1, IdempotencyKey = "blocked-capture" }, token).ConfigureAwait(false);
                        using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            deadline.CancelAfter(TimeSpan.FromSeconds(10));
                            do { await Task.Delay(20, deadline.Token).ConfigureAwait(false); blockedRun = await database.Runs.ReadAsync(owner, blockedConversation.Id, blockedRun.Id, deadline.Token).ConfigureAwait(false); } while (blockedRun.State != "paused");
                        }
                        AttemptPage blockedAttempts = await database.Attempts.EnumerateAsync(owner, blockedConversation.Id, blockedRun.Id, token: token).ConfigureAwait(false);
                        if (blockedRun.Outcome != "InvalidDataException" || blockedAttempts.Items.Single().State != "failed") throw new InvalidDataException("Diagnostic write failure masked the original attempt failure.");
                    }
                }
            }
            finally { Directory.Delete(home, true); }
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
