namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
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
                return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("Runners", "PolyPrompt transport", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Runners", "OllamaStream", "Real PolyPrompt receives NDJSON answer", StreamAsync, skip: missing, skipReason: missing ? "Set ARGUEBOT_SIMULATION_URL to the local Postman runner simulation." : null),
                    new TestCaseDescriptor("Runners", "OllamaRateLimit", "Rate limit becomes safe failure without replay", RateLimitAsync, skip: missing, skipReason: missing ? "Local Postman simulation is not configured." : null),
                    new TestCaseDescriptor("Runners", "OllamaBroken", "Truncated upstream stream does not report success", BrokenAsync, skip: missing, skipReason: missing ? "Local Postman simulation is not configured." : null)
                }) };
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
