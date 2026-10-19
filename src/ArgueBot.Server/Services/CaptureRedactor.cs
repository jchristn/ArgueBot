namespace ArgueBot.Server.Services
{
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using System;

    internal static class CaptureRedactor
    {
        internal static bool Sensitive(string name)
        {
            string value = name.Replace("-", "").Replace("_", "");
            return value.Contains("authorization", StringComparison.OrdinalIgnoreCase) || value.Contains("cookie", StringComparison.OrdinalIgnoreCase) || value.Contains("password", StringComparison.OrdinalIgnoreCase) || value.Contains("secret", StringComparison.OrdinalIgnoreCase) || value.Contains("apikey", StringComparison.OrdinalIgnoreCase) || value.Contains("token", StringComparison.OrdinalIgnoreCase) || value.Equals("code", StringComparison.OrdinalIgnoreCase);
        }
        internal static string Body(string raw, string contentType)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return "[non-JSON body omitted]";
            try
            {
                // Diagnostic bodies are an explicitly unmanaged JSON exception. Fixed API DTOs never use DOM access.
                JsonNode? node = JsonNode.Parse(raw); if (node == null) return "";
                if (node is not JsonObject && node is not JsonArray) return "[scalar JSON body omitted]";
                Redact(node); return node.ToJsonString();
            }
            catch (System.Text.Json.JsonException) { return "[invalid JSON body omitted]"; }
        }
        private static void Redact(JsonNode node)
        {
            if (node is JsonObject item)
            {
                string[] keys = new string[item.Count]; item.Select(pair => pair.Key).ToArray().CopyTo(keys, 0);
                foreach (string key in keys)
                {
                    if (Sensitive(key)) item[key] = "[redacted]";
                    else if (item[key] is JsonValue value && value.TryGetValue<string>(out string? text)) item[key] = RedactUrl(text);
                    else if (item[key] != null) Redact(item[key]!);
                }
            }
            else if (node is JsonArray array)
            {
                for (int index = 0; index < array.Count; index++)
                {
                    if (array[index] is JsonValue value && value.TryGetValue<string>(out string? text)) array[index] = RedactUrl(text);
                    else if (array[index] != null) Redact(array[index]!);
                }
            }
        }
        private static string? RedactUrl(string? value)
        {
            if (value != null && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https") && (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0))
                return new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.AbsoluteUri + " [URL credentials/query omitted]";
            return value;
        }

    }
}
