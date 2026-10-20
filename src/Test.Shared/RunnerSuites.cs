namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
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
                    new TestCaseDescriptor("Runners", "MuxCapture", "Mux streams UTF-8 answers, excludes stderr and passes literal stdin/arguments", MuxCaptureAsync, skip: fixtureMissing, skipReason: fixtureMissing ? "Set ARGUEBOT_CLI_FIXTURE to the built Test.Fixtures.dll." : null),
                    new TestCaseDescriptor("Runners", "CliCaptureFailure", "Malformed output and capture overflow terminate a live process promptly", CaptureFailureAsync, skip: fixtureMissing, skipReason: fixtureMissing ? "Set ARGUEBOT_CLI_FIXTURE to the built Test.Fixtures.dll." : null),
                    new TestCaseDescriptor("Runners", "OllamaStream", "Real PolyPrompt receives NDJSON answer", StreamAsync, skip: missing, skipReason: missing ? "Set ARGUEBOT_SIMULATION_URL to the local Postman runner simulation." : null),
                    new TestCaseDescriptor("Runners", "OllamaRateLimit", "Rate limit becomes safe failure without replay", RateLimitAsync, skip: missing, skipReason: missing ? "Local Postman simulation is not configured." : null),
                    new TestCaseDescriptor("Runners", "OllamaBroken", "Truncated upstream stream does not report success", BrokenAsync, skip: missing, skipReason: missing ? "Local Postman simulation is not configured." : null)
                }) };
            }
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
