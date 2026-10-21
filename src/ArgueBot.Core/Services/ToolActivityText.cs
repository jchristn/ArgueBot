namespace ArgueBot.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using ArgueBot.Core.Models;

    internal static class ToolActivityText
    {
        internal static string Format(ToolActivity activity, string attemptId)
        {
            StringBuilder text = new StringBuilder();
            text.Append("Local tool: ").Append(activity.Name).Append("\nStatus: ").Append(activity.State).Append("\nAttempt: ").Append(attemptId).Append("\nCall: ").Append(activity.CallId).Append("\nArguments:\n");
            AppendJson(text, activity.ArgumentsJson);
            if (activity.State == "started") text.Append("\nResult pending. If execution is interrupted, inspect the local machine before retrying.");
            else { text.Append("\nResult:\n"); AppendJson(text, activity.Result); }
            return text.ToString();
        }

        private static void AppendJson(StringBuilder text, string value)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(value)) { AppendValue(text, document.RootElement, 0); }
            }
            catch (JsonException) { text.Append(value); }
        }

        private static void AppendValue(StringBuilder text, JsonElement value, int depth)
        {
            if (text.Length > 400000) { text.Append("[Remaining tool details omitted from this preview]\n"); return; }
            if (depth > 4) { text.Append(value.GetRawText()).Append('\n'); return; }
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (text.Length > 400000) { text.Append("[Remaining tool details omitted from this preview]\n"); break; }
                    text.Append(' ', depth * 2).Append(property.Name).Append(": ");
                    if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array) text.Append('\n');
                    AppendValue(text, property.Value, depth + 1);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (text.Length > 400000) { text.Append("[Remaining tool details omitted from this preview]\n"); break; }
                    text.Append(' ', depth * 2).Append("- "); AppendValue(text, item, depth + 1);
                }
            }
            else text.Append(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()).Append('\n');
        }
    }
}
