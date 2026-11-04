namespace ArgueBot.Runners
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class EndpointAuthHandler : DelegatingHandler
    {
        private readonly RunnerProfile _Profile;
        /// <summary>Seconds from the most recent provider Retry-After header, or null when none was sent.</summary>
        internal int? RetryAfterSeconds { get; private set; }
        internal EndpointAuthHandler(RunnerProfile profile) : base(new HttpClientHandler { AllowAutoRedirect = false }) { _Profile = profile; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Uri endpoint = new Uri(_Profile.Endpoint);
            Uri destination = request.RequestUri ?? throw new DomainException(400, "invalid_endpoint", "Provider destination is missing.");
            if (destination.Scheme != endpoint.Scheme || destination.Host != endpoint.Host || destination.Port != endpoint.Port) throw new DomainException(400, "endpoint_origin_changed", "Credentials cannot be forwarded to another origin.");
            foreach (HttpAuthBinding binding in _Profile.AuthBindings)
            {
                string value = binding.CredentialId.Length > 0 ? _Profile.RuntimeCredentials[binding.CredentialId] : binding.EnvironmentVariable.Length > 0 ? Environment.GetEnvironmentVariable(binding.EnvironmentVariable) ?? "" : binding.Value;
                if (value.Length == 0 || value.Contains('\r') || value.Contains('\n')) throw new DomainException(409, "credential_unavailable", "A configured HTTP credential is unavailable or invalid.");
                if (binding.Kind == "query")
                {
                    UriBuilder builder = new UriBuilder(request.RequestUri!);
                    builder.Query = (builder.Query.Length > 1 ? builder.Query.Substring(1) + "&" : "") + Uri.EscapeDataString(binding.Name) + "=" + Uri.EscapeDataString(value);
                    request.RequestUri = builder.Uri;
                }
                else
                {
                    request.Headers.Remove(binding.Name);
                    request.Headers.TryAddWithoutValidation(binding.Name, binding.Kind == "bearer" ? "Bearer " + value : value);
                }
            }
            return SendObservedAsync(request, token);
        }
        private async Task<HttpResponseMessage> SendObservedAsync(HttpRequestMessage request, CancellationToken token)
        {
            HttpResponseMessage response = await base.SendAsync(request, token).ConfigureAwait(false);
            RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
            RetryAfterSeconds = retry?.Delta is TimeSpan delta ? (int)Math.Ceiling(delta.TotalSeconds) : retry?.Date is DateTimeOffset date ? Math.Max(0, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds)) : null;
            return response;
        }
    }
}
