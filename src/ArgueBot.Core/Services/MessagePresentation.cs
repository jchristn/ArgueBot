namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
        /// <summary>Render completed orchestration and vote envelopes as Markdown; retain incomplete or historical text exactly.</summary>
        public static string Markdown(Message message)
        {
            if (message.State != "completed" || message.ContentTruncated || (message.Kind != "question" && message.Kind != "synthesis" && message.Kind != "compaction" && message.Kind != "ratification")) return message.Content;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(message.Content))
                {
                    JsonElement root = document.RootElement;
                    string field = message.Kind == "question" ? "question" : message.Kind == "synthesis" ? "proposal" : message.Kind == "compaction" ? "summary" : "position";
                    string content = root.TryGetProperty(field, out JsonElement value) ? value.GetString() ?? message.Content : message.Content;
                    if (message.Kind == "ratification") content = "**" + content.Replace('_', ' ') + "**";
                    if (root.TryGetProperty("rationale", out JsonElement rationale) && rationale.ValueKind == JsonValueKind.String && rationale.GetString()!.Length > 0) content += "\n\n" + rationale.GetString();
                    return content;
                }
            }
            catch (Exception failure) when (failure is JsonException || failure is InvalidOperationException) { return message.Content; }
        }
    }
}
