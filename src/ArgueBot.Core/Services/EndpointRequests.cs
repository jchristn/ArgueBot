namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System;
    using System.Linq;

    /// <summary>Safe request previews without credential resolution, outbound traffic or model invocation.</summary>
    public static class EndpointRequests
    {
        /// <summary>Validate a saved endpoint and describe only its destinations and binding names.</summary>
        public static EndpointPreview Preview(EndpointProfile endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            CatalogValidation.Validate(endpoint);
            string baseUrl = endpoint.BaseUrl.TrimEnd('/');
            return new EndpointPreview
            {
                EndpointId = endpoint.Id, Revision = endpoint.Revision,
                GenerationUrl = new Uri(baseUrl + (endpoint.Protocol == "ollama" ? "/api/chat" : "/chat/completions")).AbsoluteUri,
                DiscoveryUrl = new Uri(baseUrl + (endpoint.Protocol == "ollama" ? "/api/tags" : "/models")).AbsoluteUri,
                HeaderNames = endpoint.AuthBindings.Where(binding => binding.Kind != "query").Select(binding => binding.Kind == "bearer" ? "Authorization" : binding.Name).ToList(),
                QueryNames = endpoint.AuthBindings.Where(binding => binding.Kind == "query").Select(binding => binding.Name).ToList()
            };
        }
    }
}
