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
        private static async Task<string> ExchangeAsync(Process bridge, object request)
        {
            await bridge.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request)).ConfigureAwait(false);
            await bridge.StandardInput.FlushAsync().ConfigureAwait(false);
            return await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false) ?? throw new InvalidDataException("MCP bridge closed before its response.");
        }
    }
}
