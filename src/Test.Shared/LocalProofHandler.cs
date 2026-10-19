namespace Test.Shared
{
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;

    internal sealed class LocalProofHandler : HttpMessageHandler
    {
        private readonly ArgueBotPaths _Paths;
        private readonly bool _Valid;
        internal bool CredentialBeforeProof { get; private set; }
        internal int MeRequests { get; private set; }
        internal int PairRequests { get; private set; }
        internal LocalProofHandler(ArgueBotPaths paths, bool valid) { _Paths = paths; _Valid = valid; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object response; HttpStatusCode status = HttpStatusCode.OK;
            if (request.RequestUri!.AbsolutePath == "/health")
            {
                CredentialBeforeProof = request.Headers.Authorization != null;
                string challenge = request.RequestUri.Query.Substring("?challenge=".Length);
                response = new HealthResponse { Status = "ready", Protocol = 1, Schema = DatabaseSchema.CurrentVersion, HomeId = LocalHomeIdentity.Read(_Paths)!, HomeProof = _Valid ? LocalHomeIdentity.CreateProof(_Paths, request.RequestUri.Port, challenge) : new string('0', 64) };
            }
            else if (request.RequestUri.AbsolutePath == "/v1.0/api/auth/me" && request.Headers.Authorization?.Parameter == "fixture-cli-session")
            { MeRequests++; response = new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, Role = "viewer" }; }
            else
            { PairRequests++; status = HttpStatusCode.Unauthorized; response = new ApiError { Code = "unexpected_fixture_request", Message = "The cache should not enroll another session." }; }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") });
        }
    }
}
