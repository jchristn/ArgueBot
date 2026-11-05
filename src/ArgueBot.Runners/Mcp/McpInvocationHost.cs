namespace ArgueBot.Runners.Mcp
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class McpInvocationHost
    {
        internal static async Task ServeAsync(Stream pipe, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, Func<ToolActivity, CancellationToken, Task> onTool, int remainingCharacters, CancellationToken token)
        {
            using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false, true), false, 4096, true))
            using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true))
            {
                bool initialized = false; int calls = 0; HashSet<string> identifiers = new HashSet<string>(StringComparer.Ordinal);
                for (int frameNumber = 0; frameNumber < 256; frameNumber++)
                {
                    string? frame = await McpFrames.ReadAsync(reader, token).ConfigureAwait(false); if (frame == null) return;
                    using (JsonDocument document = JsonDocument.Parse(frame))
                    {
                        JsonElement root = document.RootElement;
                        if (!root.TryGetProperty("id", out JsonElement id)) continue;
                        string method = root.GetProperty("method").GetString() ?? ""; object response;
                        if (!identifiers.Add(id.GetRawText())) response = new { jsonrpc = "2.0", id, error = new { code = -32600, message = "Duplicate request identifier; call not repeated." } };
                        else if (method == "initialize")
                        {
                            initialized = true;
                            string requested = root.TryGetProperty("params", out JsonElement initialization) && initialization.TryGetProperty("protocolVersion", out JsonElement version) ? version.GetString() ?? "" : "";
                            string negotiated = new[] { "2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25" }.Contains(requested) ? requested : "2025-11-25";
                            response = new { jsonrpc = "2.0", id, result = new { protocolVersion = negotiated, capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "arguebot-context", version = "2" } } };
                        }
                        else if (initialized && method == "ping") response = new { jsonrpc = "2.0", id, result = new { } };
                        else if (initialized && method == "tools/list") response = new { jsonrpc = "2.0", id, result = new { tools = tools.Select(tool => new { name = tool.Name, description = tool.Description, inputSchema = JsonSerializer.Deserialize<JsonElement>(tool.Schema) }).ToList() } };
                        else if (initialized && method == "tools/call" && calls < 32)
                        {
                            JsonElement parameters = root.GetProperty("params"); string name = parameters.GetProperty("name").GetString() ?? "";
                            if (!tools.Any(tool => tool.Name == name)) response = new { jsonrpc = "2.0", id, error = new { code = -32602, message = "Tool is unavailable in this invocation." } };
                            else
                            {
                                calls++;
                                ConversationToolCall call = new ConversationToolCall { Name = name, CallId = "mcp_" + calls, ArgumentsJson = parameters.TryGetProperty("arguments", out JsonElement arguments) ? arguments.GetRawText() : "{}" };
                                ToolActivity activity = new ToolActivity { CallId = call.CallId, Name = name, ArgumentsJson = call.ArgumentsJson };
                                await onTool(activity, token).ConfigureAwait(false);
                                string result = await execute(call, token).ConfigureAwait(false);
                                remainingCharacters -= call.ArgumentsJson.Length + result.Length + 256;
                                if (remainingCharacters < 0) throw new DomainException(409, "context_budget_exceeded", "Conversation tool results exceeded the invocation context budget. Increase capacity or retrieve smaller excerpts before retrying.");
                                activity.State = "completed"; activity.Result = result; await onTool(activity, token).ConfigureAwait(false);
                                response = new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text = result } } } };
                            }
                        }
                        else response = new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Method unavailable, initialization required, or invocation call limit reached." } };
                        await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), token).ConfigureAwait(false); await writer.FlushAsync(token).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
