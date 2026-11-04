namespace ArgueBot.Runners
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Explicit, bounded model discovery using the same credential and redirect policy as generation.</summary>
    public sealed class EndpointDiscoveryService
    {
        /// <summary>List advertised models without invoking a completion. Provider errors never include credentialed URLs or bodies.</summary>
        public static async Task<EndpointDiscovery> DiscoverAsync(EndpointProfile endpoint, CredentialVault vault, RequestContext context, CancellationToken token = default)
        {
            if (!endpoint.Enabled) throw new DomainException(409, "configuration_unavailable", "Enable the endpoint before checking it.");
            RunnerProfile profile = new RunnerProfile { Endpoint = endpoint.BaseUrl, AuthBindings = endpoint.AuthBindings };
            List<string> secrets = new List<string>();
            foreach (HttpAuthBinding binding in profile.AuthBindings)
            {
                string value = binding.CredentialId.Length > 0 ? await vault.ResolveAsync(context, binding.CredentialId, token).ConfigureAwait(false) : binding.EnvironmentVariable.Length > 0 ? Environment.GetEnvironmentVariable(binding.EnvironmentVariable) ?? "" : binding.Value;
                if (binding.CredentialId.Length > 0) profile.RuntimeCredentials[binding.CredentialId] = value;
                if (binding.CredentialId.Length > 0 || binding.EnvironmentVariable.Length > 0) secrets.Add(value);
            }
            try
            {
                string path = endpoint.BaseUrl.TrimEnd('/') + (endpoint.Protocol == "ollama" ? "/api/tags" : "/models");
                using (HttpClient client = new HttpClient(new EndpointAuthHandler(profile)) { Timeout = TimeSpan.FromSeconds(Math.Min(endpoint.TimeoutSeconds, 30)), MaxResponseContentBufferSize = 1048576 })
                using (HttpResponseMessage response = await client.GetAsync(path, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new DomainException(502, "discovery_failed", "The endpoint did not accept the model-list request (HTTP " + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture) + "). Check its protocol, base URL and credentials. Manual model IDs remain supported.");
                    using (JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)))
                    {
                        JsonElement items = document.RootElement.GetProperty(endpoint.Protocol == "ollama" ? "models" : "data");
                        EndpointDiscovery result = new EndpointDiscovery { EndpointId = endpoint.Id, Revision = endpoint.Revision, CheckedUtc = DateTime.UtcNow };
                        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (JsonElement item in items.EnumerateArray())
                        {
                            string name = item.GetProperty(endpoint.Protocol == "ollama" ? "name" : "id").GetString() ?? "";
                            if (name.Length == 0 || name.Length > 512 || name.Any(char.IsControl) || secrets.Any(secret => secret.Length > 0 && name.Contains(secret, StringComparison.Ordinal))) continue;
                            if (!seen.Add(name)) continue;
                            if (result.Models.Count == 1000) { result.Truncated = true; break; }
                            result.Models.Add(name);
                        }
                        return result;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            catch (DomainException) { throw; }
            catch (Exception failure) when (failure is HttpRequestException || failure is OperationCanceledException || failure is JsonException || failure is InvalidOperationException || failure is KeyNotFoundException)
            { throw new DomainException(502, "discovery_failed", "Model discovery failed or exceeded its 30-second/1-MiB limit. Check reachability, protocol and credentials, or enter a model ID manually."); }
            finally { profile.RuntimeCredentials.Clear(); secrets.Clear(); }
        }
    }
}
