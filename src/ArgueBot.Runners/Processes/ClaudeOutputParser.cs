namespace ArgueBot.Runners.Processes
{
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class ClaudeOutputParser
    {
        private readonly Dictionary<string, string> _Messages = new Dictionary<string, string>(StringComparer.Ordinal);
        private string _CurrentMessage = "legacy-message";
        private string _LastAnswerMessage = "";
        private int _MessageNumber;
        private bool _ResultSeen;

        internal async Task EmitAsync(string line, Func<string, CancellationToken, Task> onText, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(line))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an event object.");
                    // Tool results and metadata are not assistant text. Their arbitrary payloads must not be deserialized as message blocks.
                    switch (ReadString(root, "type"))
                    {
                        case "stream_event":
                            if (root.TryGetProperty("parent_tool_use_id", out JsonElement parent) && parent.ValueKind == JsonValueKind.String) return;
                            if (!root.TryGetProperty("event", out JsonElement item)) throw new JsonException("Missing $.event.");
                            string? kind = ReadString(item, "type");
                            if (kind == "message_start")
                            {
                                _MessageNumber++;
                                _CurrentMessage = item.TryGetProperty("message", out JsonElement message) ? ReadString(message, "id") ?? "message-" + _MessageNumber : "message-" + _MessageNumber;
                            }
                            else if (kind == "content_block_start" && item.TryGetProperty("content_block", out JsonElement block) && ReadString(block, "type") == "text")
                                await DeltaAsync(ReadString(block, "text"), onText, token).ConfigureAwait(false);
                            else if (item.TryGetProperty("delta", out JsonElement delta) && (ReadString(delta, "type") == "text_delta" || ReadString(delta, "type") == null))
                                await DeltaAsync(ReadString(delta, "text"), onText, token).ConfigureAwait(false);
                            break;
                        case "assistant":
                            if (root.TryGetProperty("parent_tool_use_id", out JsonElement ancestor) && ancestor.ValueKind == JsonValueKind.String) return;
                            if (!root.TryGetProperty("message", out JsonElement assistant) || !assistant.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array) throw new JsonException("Expected an array at $.message.content.");
                            StringBuilder body = new StringBuilder();
                            foreach (JsonElement part in content.EnumerateArray()) if (ReadString(part, "type") == "text") body.Append(ReadString(part, "text"));
                            await SnapshotAsync(ReadString(assistant, "id") ?? _CurrentMessage, body.ToString(), onText, token).ConfigureAwait(false);
                            break;
                        case "result":
                            _ResultSeen = true;
                            if ((root.TryGetProperty("is_error", out JsonElement error) && error.ValueKind == JsonValueKind.True) || (ReadString(root, "subtype") ?? "").StartsWith("error", StringComparison.Ordinal)) throw new DomainException(502, "agent_result_error", "Claude reported an unsuccessful result. Inspect the retained process diagnostics.");
                            string? result = ReadString(root, "result");
                            if (string.IsNullOrWhiteSpace(result)) throw new DomainException(502, "empty_answer", "Claude completed without a final answer. Inspect the retained process diagnostics.");
                            await SnapshotAsync(_LastAnswerMessage.Length == 0 ? "result" : _LastAnswerMessage, result, onText, token).ConfigureAwait(false);
                            break;
                        case "text_delta":
                            await DeltaAsync(ReadString(root, "text"), onText, token).ConfigureAwait(false);
                            break;
                    }
                }
            }
            catch (JsonException exception) { throw new InvalidDataException("Agent emitted malformed JSONL or an invalid assistant event.", exception); }
        }
        internal void Complete()
        {
            if (!_ResultSeen) throw new DomainException(502, _Messages.Count == 0 ? "empty_answer" : "incomplete_answer", "Claude output ended without a final result. Inspect the retained process diagnostics.");
        }
        private static string? ReadString(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an object containing " + name + ".");
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
            if (value.ValueKind != JsonValueKind.String) throw new JsonException("Expected a string for " + name + ".");
            return value.GetString();
        }
        private Task DeltaAsync(string? text, Func<string, CancellationToken, Task> onText, CancellationToken token)
        {
            if (string.IsNullOrEmpty(text)) return Task.CompletedTask;
            _Messages.TryGetValue(_CurrentMessage, out string? prior);
            return SnapshotAsync(_CurrentMessage, (prior ?? "") + text, onText, token);
        }
        private async Task SnapshotAsync(string id, string text, Func<string, CancellationToken, Task> onText, CancellationToken token)
        {
            if (text.Length == 0) return;
            bool existing = _Messages.TryGetValue(id, out string? prior);
            prior ??= "";
            if (!text.StartsWith(prior, StringComparison.Ordinal)) throw new InvalidDataException("Agent message snapshot conflicts with earlier text for the same Claude message.");
            string suffix = text.Substring(prior.Length);
            if (suffix.Length == 0) return;
            if (!existing && _Messages.Count > 0) await onText("\n\n", token).ConfigureAwait(false);
            await onText(suffix, token).ConfigureAwait(false);
            _Messages[id] = text;
            _LastAnswerMessage = id;
        }
    }
}
