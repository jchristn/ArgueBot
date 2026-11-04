namespace ArgueBot.Runners.Mcp
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Processes;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipes;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class McpInvocationRunner
    {
        internal static async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token)
        {
            string name = "abm-" + Guid.NewGuid().ToString("N");
            RunnerProfile child = JsonSerializer.Deserialize<RunnerProfile>(JsonSerializer.Serialize(profile))!;
            foreach (KeyValuePair<string, string> variable in profile.RuntimeEnvironment) child.RuntimeEnvironment[variable.Key] = variable.Value;
            List<string> bridgeArguments = profile.RuntimeMcpCommand.Skip(1).Concat(new[] { "--context-mcp", name }).ToList();
            string command = profile.RuntimeMcpCommand[0];
            if (profile.Provider == "claude")
            {
                child.Arguments.Add("--strict-mcp-config"); child.Arguments.Add("--mcp-config");
                child.Arguments.Add(JsonSerializer.Serialize(new { mcpServers = new { arguebot = new { command, args = bridgeArguments } } }));
            }
            else
            {
                child.Arguments.Add("--config"); child.Arguments.Add("mcp_servers.arguebot.command=" + JsonSerializer.Serialize(command));
                child.Arguments.Add("--config"); child.Arguments.Add("mcp_servers.arguebot.args=" + JsonSerializer.Serialize(bridgeArguments));
            }
            using (CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (NamedPipeServerStream pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                long remaining = (long)profile.ContextMaxCharacters - instructions.Length - history.Sum(message => (long)message.Content.Length + message.Speaker.Length + 32) - tools.Sum(tool => (long)tool.Schema.Length + tool.Description.Length + tool.Name.Length) - 1024;
                if (remaining < 0) throw new DomainException(409, "context_budget_exceeded", "The MCP prompt and tool definitions exceed the model context budget.");
                Task host = ServeAsync(pipe, tools, execute, onTool, (int)remaining, lifetime);
                try { await new CliRunner().GenerateAsync(child, instructions + "\nUse the arguebot MCP conversation tools for original evidence or moderator clarification. They are scoped to this invocation.", history, onText, lifetime.Token).ConfigureAwait(false); }
                finally
                {
                    lifetime.Cancel(); pipe.Close();
                    try { await host.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                    catch (IOException) when (lifetime.IsCancellationRequested) { }
                }
            }
        }
        private static async Task ServeAsync(NamedPipeServerStream pipe, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, Func<ToolActivity, CancellationToken, Task> onTool, int remainingCharacters, CancellationTokenSource lifetime)
        {
            try
            {
                await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                await McpInvocationHost.ServeAsync(pipe, tools, execute, onTool, remainingCharacters, lifetime.Token).ConfigureAwait(false);
            }
            catch { lifetime.Cancel(); throw; }
        }
    }
}
