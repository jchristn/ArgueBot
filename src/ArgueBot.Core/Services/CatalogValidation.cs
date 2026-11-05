namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    internal static class CatalogValidation
    {
        internal static void Validate(EndpointProfile item)
        {
            Name(item.Name);
            if (item.Protocol != "ollama" && item.Protocol != "openai") throw Invalid("Choose Ollama native chat or OpenAI-compatible chat completions.");
            if (!Uri.TryCreate(item.BaseUrl, UriKind.Absolute, out Uri? uri) || (uri.Scheme != "https" && uri.Scheme != "http") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || item.BaseUrl.Length > 4096) throw Invalid("Use an HTTP(S) base URL without credentials, query or fragment.");
            if (item.MaxConcurrentInvocations < 1 || item.MaxConcurrentInvocations > 64) throw Invalid("Endpoint capacity must be 1 through 64 simultaneous generations.");
            if (item.TimeoutSeconds < 1 || item.TimeoutSeconds > 3600) throw Invalid("Timeout must be 1 through 3600 seconds.");
            Bindings(item.AuthBindings);
            if (item.AuthBindings.Any(binding => binding.CredentialId.Length > 0 || binding.EnvironmentVariable.Length > 0) && !uri.IsLoopback && uri.Scheme != "https") throw Invalid("Remote credentialed endpoints require HTTPS.");
        }
        internal static void Validate(ModelProfile item)
        {
            Name(item.Name);
            if (item.EndpointId.Length == 0) throw Invalid("Choose the HTTP endpoint this model runs on. CLI programs are configured as agents.");
            if (item.Model.Length > 1000 || string.IsNullOrWhiteSpace(item.Model)) throw Invalid("Choose a provider model identifier up to 1000 characters.");
            if (item.ContextMaxCharacters < 8192 || item.ContextMaxCharacters > 16777216) throw Invalid("Model context must be 8192 through 16777216 characters.");
            if (item.MaxOutputBytes < 1024 || item.MaxOutputBytes > 67108864) throw Invalid("Captured model output must be 1024 through 67108864 bytes.");
            ValidateGeneration(item.Generation, true);
        }
        internal static void ValidateGeneration(ModelGenerationSettings settings, bool http)
        {
            if (!http && (settings.MaxOutputTokens.HasValue || settings.Temperature.HasValue || settings.TopP.HasValue)) throw Invalid("Token and sampling overrides apply to HTTP models. Configure supported CLI flags as literal agent arguments.");
            if (settings.MaxOutputTokens.HasValue && (settings.MaxOutputTokens.Value < 1 || settings.MaxOutputTokens.Value > 1048576)) throw Invalid("Generated tokens must be 1 through 1048576, or omitted for the adapter default.");
            if (settings.Temperature.HasValue && (!double.IsFinite(settings.Temperature.Value) || settings.Temperature.Value < 0 || settings.Temperature.Value > 2)) throw Invalid("Temperature must be zero through two, or omitted.");
            if (settings.TopP.HasValue && (!double.IsFinite(settings.TopP.Value) || settings.TopP.Value < 0 || settings.TopP.Value > 1)) throw Invalid("Top-p must be zero through one, or omitted.");
        }
        internal static void ValidateEnvironment(List<string> names)
        {
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names.Count > 64 || names.Any(name => name == null || !Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]{0,127}$") || !unique.Add(name))) throw Invalid("Use at most 64 unique environment variable names, without values or punctuation.");
        }
        internal static void Bindings(List<HttpAuthBinding> bindings)
        {
            if (bindings.Count > 32) throw Invalid("Use at most 32 HTTP bindings.");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (HttpAuthBinding binding in bindings)
            {
                if (binding == null) throw Invalid("HTTP bindings cannot contain null items.");
                if (!new[] { "bearer", "api_key", "header", "query" }.Contains(binding.Kind)) throw Invalid("Choose bearer, api_key, header or query authentication.");
                if (binding.Kind == "bearer") binding.Name = "Authorization";
                if (!Regex.IsMatch(binding.Name, "^[A-Za-z0-9_-]{1,128}$") || !names.Add((binding.Kind == "query" ? "query:" : "header:") + binding.Name)) throw Invalid("Use unique HTTP header or query names without whitespace.");
                if (ProtectedHeaders.Contains(binding.Name.ToLowerInvariant())) throw Invalid("This protocol-controlled HTTP header cannot be overridden.");
                int sources = (binding.CredentialId.Length > 0 ? 1 : 0) + (binding.EnvironmentVariable.Length > 0 ? 1 : 0) + (binding.Value.Length > 0 ? 1 : 0);
                if (sources != 1 || binding.Value.Length > 4096 || binding.Value.Any(char.IsControl)) throw Invalid("Choose exactly one credential, environment reference or nonsecret value without control characters.");
                if ((binding.Kind != "header" || binding.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) && binding.Value.Length > 0) throw Invalid("Authentication values must use a stored credential or environment reference.");
                if (binding.EnvironmentVariable.Length > 0 && !Regex.IsMatch(binding.EnvironmentVariable, "^[A-Za-z_][A-Za-z0-9_]{0,127}$")) throw Invalid("Use a valid service environment variable name.");
            }
        }
        /// <summary>Validate the header or query name a typed HTTP credential is sent under.</summary>
        internal static void ParameterName(string placement, string name)
        {
            if (!Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,128}$")) throw new DomainException(400, "invalid_credential", "Enter a " + placement + " name of letters, digits, hyphens or underscores, up to 128 characters.", "parameterName");
            if (placement == "header" && ProtectedHeaders.Contains(name.ToLowerInvariant())) throw new DomainException(400, "invalid_credential", "This protocol-controlled HTTP header cannot be set by a credential.", "parameterName");
        }
        /// <summary>Make bindings that use typed HTTP credentials follow the credential's placement and name, and reject CLI-only kinds.</summary>
        internal static void Place(List<HttpAuthBinding> bindings, IReadOnlyDictionary<string, CredentialMetadata> credentials)
        {
            foreach (HttpAuthBinding binding in bindings)
            {
                if (binding == null || binding.CredentialId.Length == 0) continue;
                if (!credentials.TryGetValue(binding.CredentialId, out CredentialMetadata? credential)) throw Invalid("Choose an existing stored credential for each binding.");
                if (credential.Kind == "claude_oauth" || credential.Kind == "codex_auth_json") throw new DomainException(400, "credential_kind_mismatch", "Claude Code tokens and Codex logins sign in CLI agents. Choose an HTTP credential for endpoint authentication.");
                if (credential.Kind == "http_bearer") { binding.Kind = "bearer"; binding.Name = "Authorization"; }
                else if (credential.Kind == "http_header") { binding.Kind = "header"; binding.Name = credential.ParameterName; }
                else if (credential.Kind == "http_query") { binding.Kind = "query"; binding.Name = credential.ParameterName; }
            }
        }
        private static readonly string[] ProtectedHeaders = new[] { "host", "content-length", "connection", "transfer-encoding", "content-type", "accept", "cookie", "proxy-authorization" };
        private static void Name(string name) { if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw Invalid("Use a display name of 1 through 200 characters without control characters."); }
        private static DomainException Invalid(string message) { return new DomainException(400, "invalid_configuration", message); }
    }
}
