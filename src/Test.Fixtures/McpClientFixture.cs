namespace ArgueBot.Tests.Fixtures
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;

    internal static class McpClientFixture
    {
        internal static async Task<int> RunAsync(string[] args)
        {
            string command; List<string> arguments;
            int config = Array.IndexOf(args, "--mcp-config");
            if (config >= 0)
            {
                using (JsonDocument document = JsonDocument.Parse(args[config + 1]))
                {
                    JsonElement server = document.RootElement.GetProperty("mcpServers").GetProperty("arguebot");
                    command = server.GetProperty("command").GetString()!;
                    arguments = server.GetProperty("args").EnumerateArray().Select(value => value.GetString()!).ToList();
                }
            }
            else
            {
                command = JsonSerializer.Deserialize<string>(args.First(value => value.StartsWith("mcp_servers.arguebot.command=", StringComparison.Ordinal)).Split('=', 2)[1])!;
                arguments = JsonSerializer.Deserialize<List<string>>(args.First(value => value.StartsWith("mcp_servers.arguebot.args=", StringComparison.Ordinal)).Split('=', 2)[1])!;
            }
            ProcessStartInfo start = new ProcessStartInfo(command) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using (Process bridge = Process.Start(start)!)
            {
                try
                {
                    string initialize = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "fixture", version = "1" } } }).ConfigureAwait(false);
                    if (!initialize.Contains("2025-03-26", StringComparison.Ordinal)) return 31;
                    string list = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 2, method = "tools/list" }).ConfigureAwait(false);
                    if (!list.Contains("get_turn", StringComparison.Ordinal) || list.Contains("save_credentials", StringComparison.Ordinal)) return 32;
                    string denied = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "save_credentials", arguments = new { } } }).ConfigureAwait(false);
                    if (!denied.Contains("-32602", StringComparison.Ordinal)) return 33;
                    string result = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "get_turn", arguments = new { id = "fixture-original" } } }).ConfigureAwait(false);
                    if (!result.Contains("Original evidence", StringComparison.Ordinal)) return 34;
                    string duplicate = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "get_turn", arguments = new { id = "fixture-original" } } }).ConfigureAwait(false);
                    if (!duplicate.Contains("-32600", StringComparison.Ordinal)) return 35;
                    Console.WriteLine(args[0] == "mcp-claude" ? "{\"type\":\"result\",\"result\":\"MCP evidence verified.\"}" : "{\"type\":\"item.completed\",\"item\":{\"id\":\"final\",\"type\":\"agent_message\",\"text\":\"MCP evidence verified.\"}}");
                    return 0;
                }
                finally { bridge.StandardInput.Close(); if (!bridge.HasExited) bridge.Kill(true); await bridge.WaitForExitAsync().ConfigureAwait(false); }
            }
        }
        internal static async Task<int> ResourcesAsync(string[] args)
        {
            string command; List<string> arguments;
            int config = Array.IndexOf(args, "--mcp-config");
            if (config >= 0)
            {
                using (JsonDocument document = JsonDocument.Parse(args[config + 1]))
                {
                    JsonElement server = document.RootElement.GetProperty("mcpServers").GetProperty("arguebot");
                    command = server.GetProperty("command").GetString()!;
                    arguments = server.GetProperty("args").EnumerateArray().Select(value => value.GetString()!).ToList();
                }
            }
            else
            {
                command = JsonSerializer.Deserialize<string>(args.First(value => value.StartsWith("mcp_servers.arguebot.command=", StringComparison.Ordinal)).Split('=', 2)[1])!;
                arguments = JsonSerializer.Deserialize<List<string>>(args.First(value => value.StartsWith("mcp_servers.arguebot.args=", StringComparison.Ordinal)).Split('=', 2)[1])!;
            }
            ProcessStartInfo start = new ProcessStartInfo(command) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using (Process bridge = Process.Start(start)!)
            {
                try
                {
                    await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "fixture", version = "1" } } }).ConfigureAwait(false);
                    string list = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 2, method = "tools/list" }).ConfigureAwait(false);
                    if (!list.Contains("\"read_resource\"", StringComparison.Ordinal) || !list.Contains("\"append_resource\"", StringComparison.Ordinal)) return 41;
                    string read = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "read_resource", arguments = new { name = "billing" } } }).ConfigureAwait(false);
                    string readText = ToolText(read);
                    if (!readText.Contains("Invoices go out on the 1st", StringComparison.Ordinal) || !readText.Contains("議論", StringComparison.Ordinal)) return 42;
                    string append = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "append_resource", arguments = new { name = "debate-notes", text = "Fixture note via MCP." } } }).ConfigureAwait(false);
                    using (JsonDocument appended = JsonDocument.Parse(ToolText(append))) { if (!appended.RootElement.GetProperty("success").GetBoolean()) return 43; }
                    string stale = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "update_resource", arguments = new { name = "billing", revision = 99, body = "Overwrite." } } }).ConfigureAwait(false);
                    if (!ToolText(stale).Contains("revision_conflict", StringComparison.Ordinal)) return 44;
                    Console.WriteLine(args[0] == "mcp-resources-claude" ? "{\"type\":\"result\",\"result\":\"MCP resources verified.\"}" : "{\"type\":\"item.completed\",\"item\":{\"id\":\"final\",\"type\":\"agent_message\",\"text\":\"MCP resources verified.\"}}");
                    return 0;
                }
                finally { bridge.StandardInput.Close(); if (!bridge.HasExited) bridge.Kill(true); await bridge.WaitForExitAsync().ConfigureAwait(false); }
            }
        }
        /// <summary>Ask the user over the MCP bridge and finish with the answer the held call returned.</summary>
        internal static async Task<int> QuestionsAsync(string[] args)
        {
            int config = Array.IndexOf(args, "--mcp-config");
            string command; List<string> arguments;
            if (config >= 0)
            {
                if (Environment.GetEnvironmentVariable("MCP_TOOL_TIMEOUT") is not string timeout || long.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture) < 1800000) return 50;
                if (Array.IndexOf(args, "--disallowedTools") < 0 || args[Array.IndexOf(args, "--disallowedTools") + 1] != "AskUserQuestion") return 51;
                using (JsonDocument document = JsonDocument.Parse(args[config + 1]))
                {
                    JsonElement server = document.RootElement.GetProperty("mcpServers").GetProperty("arguebot");
                    command = server.GetProperty("command").GetString()!;
                    arguments = server.GetProperty("args").EnumerateArray().Select(value => value.GetString()!).ToList();
                }
            }
            else
            {
                if (!args.Any(value => value.StartsWith("mcp_servers.arguebot.tool_timeout_sec=", StringComparison.Ordinal) && int.Parse(value.Split('=', 2)[1], System.Globalization.CultureInfo.InvariantCulture) >= 1800)) return 52;
                command = JsonSerializer.Deserialize<string>(args.First(value => value.StartsWith("mcp_servers.arguebot.command=", StringComparison.Ordinal)).Split('=', 2)[1])!;
                arguments = JsonSerializer.Deserialize<List<string>>(args.First(value => value.StartsWith("mcp_servers.arguebot.args=", StringComparison.Ordinal)).Split('=', 2)[1])!;
            }
            ProcessStartInfo start = new ProcessStartInfo(command) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using (Process bridge = Process.Start(start)!)
            {
                try
                {
                    await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = "fixture", version = "1" } } }).ConfigureAwait(false);
                    string list = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 2, method = "tools/list" }).ConfigureAwait(false);
                    if (!list.Contains("\"ask_user\"", StringComparison.Ordinal) || list.Contains("request_clarification", StringComparison.Ordinal)) return 53;
                    string invalid = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "ask_user", arguments = new { questions = new object[] { new { prompt = "Pick", kind = "single_choice", options = new object[] { new { id = "only", label = "Only one" } } } } } } }).ConfigureAwait(false);
                    if (!ToolText(invalid).Contains("invalid_question", StringComparison.Ordinal)) return 54;
                    string asked = await ExchangeAsync(bridge, new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "ask_user", arguments = new { context = "Scope decides the design.", questions = new object[] { new { key = "scope", header = "Scope", prompt = "Which environments?", kind = "single_choice", options = new object[] { new { id = "prod", label = "Production only", recommended = true }, new { id = "all", label = "Every environment" } } } } } } }).ConfigureAwait(false);
                    string text = ToolText(asked);
                    if (!text.Contains("\"answered\"", StringComparison.Ordinal) || !text.Contains("Production only", StringComparison.Ordinal)) return 55;
                    Console.WriteLine(args[0] == "mcp-questions-claude" ? "{\"type\":\"result\",\"result\":\"MCP question answered: Production only.\"}" : "{\"type\":\"item.completed\",\"item\":{\"id\":\"final\",\"type\":\"agent_message\",\"text\":\"MCP question answered: Production only.\"}}");
                    return 0;
                }
                finally { bridge.StandardInput.Close(); if (!bridge.HasExited) bridge.Kill(true); await bridge.WaitForExitAsync().ConfigureAwait(false); }
            }
        }
        private static string ToolText(string response)
        {
            using (JsonDocument document = JsonDocument.Parse(response))
                return document.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        }
        private static async Task<string> ExchangeAsync(Process bridge, object request)
        {
            await bridge.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
            await bridge.StandardInput.FlushAsync().ConfigureAwait(false);
            return await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false) ?? throw new InvalidDataException("MCP bridge closed before its response.");
        }
    }
}
