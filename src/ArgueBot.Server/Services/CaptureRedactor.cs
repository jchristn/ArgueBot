namespace ArgueBot.Server.Services
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    internal static class CaptureRedactor
    {
        internal static bool Sensitive(string name)
        {
            string value = name.Replace("-", "").Replace("_", "");
            return value.Contains("signature", StringComparison.OrdinalIgnoreCase) || value.Contains("authorization", StringComparison.OrdinalIgnoreCase) || value.Contains("cookie", StringComparison.OrdinalIgnoreCase) || value.Contains("password", StringComparison.OrdinalIgnoreCase) || value.Contains("secret", StringComparison.OrdinalIgnoreCase) || value.Contains("apikey", StringComparison.OrdinalIgnoreCase) || value.Contains("token", StringComparison.OrdinalIgnoreCase) || value.Equals("code", StringComparison.OrdinalIgnoreCase);
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
                    else if (item[key] is JsonValue value && value.TryGetValue<string>(out string? text))
                    {
                        string name = key.Replace("_", "").Replace("-", "");
                        bool reference = name.EndsWith("environmentvariable", StringComparison.OrdinalIgnoreCase) || name.EndsWith("credentialreference", StringComparison.OrdinalIgnoreCase) || name.EndsWith("connectionreference", StringComparison.OrdinalIgnoreCase);
                        item[key] = reference && !string.IsNullOrEmpty(text) && !Regex.IsMatch(text, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant) ? "[invalid credential reference omitted]" : Text(text);
                    }
                    else if (item[key] != null) Redact(item[key]!);
                }
            }
            else if (node is JsonArray array)
            {
                for (int index = 0; index < array.Count; index++)
                {
                    if (array[index] is JsonValue value && value.TryGetValue<string>(out string? text))
                    {
                        bool followsSecretFlag = index > 0 && array[index - 1] is JsonValue previous && previous.TryGetValue<string>(out string? flag) && flag != null && flag.StartsWith("-", StringComparison.Ordinal) && Sensitive(flag);
                        array[index] = followsSecretFlag ? "[redacted]" : Text(text);
                    }
                    else if (array[index] != null) Redact(array[index]!);
                }
            }
        }
        internal static string? Text(string? value)
        {
            if (value != null && Regex.IsMatch(value, @"(?:password|pwd|secret|api[-_]?key|token|authorization)\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return "[credential assignment omitted]";
            if (value != null && Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https") && (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0))
                return new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.AbsoluteUri + " [URL credentials/query omitted]";
            return value;
        }

    }
}
