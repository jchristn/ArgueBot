namespace Test.Shared
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class McpChecks
    {
        internal static async Task RunAsync(CancellationToken token)
        {
            string fixture = Environment.GetEnvironmentVariable("ARGUEBOT_CLI_FIXTURE")!;
            string server = fixture.Replace("Test.Fixtures", "ArgueBot.Server", StringComparison.Ordinal);
            if (!File.Exists(server)) throw new FileNotFoundException("Build the service before testing the native MCP bridge.", server);
            foreach (string provider in new[] { "claude", "codex" })
            {
                RunnerProfile profile = new RunnerProfile { Kind = "cli", Provider = provider, Executable = "dotnet", Arguments = new List<string> { fixture, "mcp-" + provider }, TimeoutSeconds = 20 };
                profile.RuntimeMcpCommand.AddRange(new[] { "dotnet", server });
                int calls = 0; int activities = 0; StringBuilder output = new StringBuilder();
                await new RunnerRouter(true).GenerateWithContextAsync(profile, "Review evidence.", new List<Message>(), new List<ConversationToolDefinition> { new ConversationToolDefinition { Name = "get_turn", Description = "Retrieve an original turn.", Schema = "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}},\"required\":[\"id\"]}" } },
                    (call, _) => { calls++; if (call.Name != "get_turn" || !call.ArgumentsJson.Contains("fixture-original", StringComparison.Ordinal)) throw new InvalidDataException("Incorrect scoped tool dispatch."); return Task.FromResult("Original evidence 議論"); }, false,
                    (text, _) => { output.Append(text); return Task.CompletedTask; }, (_, _) => { activities++; return Task.CompletedTask; }, token).ConfigureAwait(false);
                if (calls != 1 || activities != 2 || output.ToString() != "MCP evidence verified.") throw new InvalidDataException(provider + " did not complete the native MCP evidence exchange exactly once.");
                profile.ContextMaxCharacters = 8192;
                try
                {
                    await new RunnerRouter(true).GenerateWithContextAsync(profile, "Review evidence.", new List<Message>(), new List<ConversationToolDefinition> { new ConversationToolDefinition { Name = "get_turn", Description = "Retrieve evidence.", Schema = "{\"type\":\"object\"}" } },
                        (_, _) => Task.FromResult(new string('x', 9000)), false, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, token).ConfigureAwait(false);
                    throw new InvalidDataException("Native MCP accepted evidence beyond the invocation context budget.");
                }
                catch (DomainException failure) when (failure.Code == "context_budget_exceeded") { }

            }
        }
    }
}
