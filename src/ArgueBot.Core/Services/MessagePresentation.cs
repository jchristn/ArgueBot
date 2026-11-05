namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>Readable presentation of completed protocol envelopes without changing stored evidence.</summary>
    public static class MessagePresentation
    {
        /// <summary>Hide a tool's "started" record when its finished record for the same attempt and call is in the same list, so one call reads as one card. Storage keeps both records.</summary>
        public static List<Message> MergeToolActivity(IEnumerable<Message> messages)
        {
            List<Message> list = messages.ToList();
            HashSet<string> finished = new HashSet<string>(list.Where(message => message.Kind == "tool" && ToolStatus(message) is string status && status != "started").Select(ToolKey).Where(key => key.Length > 0), StringComparer.Ordinal);
            return list.Where(message => !(message.Kind == "tool" && ToolStatus(message) == "started" && finished.Contains(ToolKey(message)))).ToList();
        }
        private static string? ToolStatus(Message message) { Match match = Regex.Match(message.Content, @"^Status: (\S+)$", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100)); return match.Success ? match.Groups[1].Value : null; }
        private static string ToolKey(Message message)
        {
            Match attempt = Regex.Match(message.Content, @"^Attempt: (\S+)$", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
            Match call = Regex.Match(message.Content, @"^Call: (\S+)$", RegexOptions.Multiline, TimeSpan.FromMilliseconds(100));
            return attempt.Success && call.Success ? attempt.Groups[1].Value + "|" + call.Groups[1].Value : "";
        }
        /// <summary>
        /// Render orchestration and vote replies (JSON objects, optionally inside a code fence) as readable Markdown.
        /// Complete replies show their main field and rationale. Previews and streaming replies that are cut off
        /// show the readable part of the main field. Anything else is returned exactly.
        /// </summary>
        public static string Markdown(Message message)
        {
            ArgumentNullException.ThrowIfNull(message);
            string? field = message.Kind == "question" ? "question" : message.Kind == "synthesis" ? "proposal" : message.Kind == "compaction" ? "summary" : message.Kind == "ratification" ? "position" : null;
            if (field == null || message.State == "failed" || message.State == "interrupted") return message.Content;
            string text = Unfence(message.Content);
            if (!text.TrimStart().StartsWith('{')) return message.Content;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(text))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(field, out JsonElement value) || value.ValueKind != JsonValueKind.String) return message.Content;
                    string content = value.GetString() ?? "";
                    if (message.Kind == "ratification") content = "**" + content.Replace('_', ' ') + "**";
                    if (root.TryGetProperty("rationale", out JsonElement rationale) && rationale.ValueKind == JsonValueKind.String && rationale.GetString()!.Length > 0) content += "\n\n" + rationale.GetString();
                    return content;
                }
            }
            catch (JsonException)
            {
                string? partial = PartialString(text, field, out bool complete);
                if (partial == null) return message.Content;
                return (message.Kind == "ratification" ? "**" + partial.Replace('_', ' ') + "**" : partial) + (complete ? "" : "\u2026");
            }
        }

        /// <summary>Remove one surrounding Markdown code fence (```json ... ```), including an unterminated one.</summary>
        internal static string Unfence(string content)
        {
            string trimmed = content.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return content;
            int newline = trimmed.IndexOf('\n', StringComparison.Ordinal);
            if (newline < 0) return content;
            string body = trimmed.Substring(newline + 1);
            int close = body.LastIndexOf("```", StringComparison.Ordinal);
            return close >= 0 ? body.Substring(0, close) : body;
        }

        /// <summary>The decoded text of a JSON string field in incomplete JSON, or null when the field has not started.</summary>
        internal static string? PartialString(string json, string field, out bool complete)
        {
            complete = false;
            Match match = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"");
            if (!match.Success) return null;
            StringBuilder result = new StringBuilder();
            for (int index = match.Index + match.Length; index < json.Length; index++)
            {
                char character = json[index];
                if (character == '"') { complete = true; break; }
                if (character != '\\') { result.Append(character); continue; }
                if (++index >= json.Length) break;
                char escape = json[index];
                if (escape == 'n') result.Append('\n');
                else if (escape == 't') result.Append('\t');
                else if (escape == 'r') { }
                else if (escape == 'b' || escape == 'f') { }
                else if (escape == 'u')
                {
                    if (index + 4 >= json.Length) break;
                    if (int.TryParse(json.AsSpan(index + 1, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code)) result.Append((char)code);
                    index += 4;
                }
                else result.Append(escape);
            }
            string text = result.ToString();
            return text.Length == 0 ? null : text;
        }
    }
}
