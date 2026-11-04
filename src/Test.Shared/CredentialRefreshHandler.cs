namespace Test.Shared
{
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class CredentialRefreshHandler : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        internal bool Reject { get; set; }
        /// <summary>Simulate an unreachable login service (connect), or a refused request (server_error returns 503, rate_limited returns 429).</summary>
        internal string Failure { get; set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure == "connect") throw new HttpRequestException(HttpRequestError.ConnectionError, "Fictional connection refusal.");
            if (Failure == "server_error") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
            if (Failure == "invalid_body") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not a token response</html>") });
            if (Failure == "rate_limited") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") });
            return Task.FromResult(new HttpResponseMessage(Reject ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fictional-refreshed-access\",\"refresh_token\":\"fictional-rotated-refresh\"}") });
        }
    }
}
