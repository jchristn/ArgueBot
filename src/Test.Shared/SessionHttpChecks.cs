namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;

    internal static class SessionHttpChecks
    {
        internal static async Task RunAsync(string endpoint, string home, CancellationToken token)
        {
            using (HttpClient http = new HttpClient(LocalServiceConnector.CreateHttpHandler(home)) { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(10) })
            {
                for (int attempt = 0; attempt < 21; attempt++)
                {
                    using (HttpResponseMessage rejected = await http.PostAsJsonAsync("v1.0/api/auth/login", new LoginRequest { TenantId = "ten_local", Email = "missing@example.invalid", PasswordSha256 = new string('D', 64) }, token).ConfigureAwait(false))
                    { if ((int)rejected.StatusCode != (attempt < 20 ? 401 : 429)) throw new InvalidDataException("Password exchange did not enforce its bounded service-wide attempt window."); }
                }
                string code = await File.ReadAllTextAsync(Path.Combine(home, "runtime", "pairing-code"), token).ConfigureAwait(false);
                using (HttpResponseMessage paired = await http.PostAsJsonAsync("v1.0/api/auth/pair", new { code }, token).ConfigureAwait(false))
                {
                    paired.EnsureSuccessStatusCode();
                    SessionEnrollmentResponse enrolled = await paired.Content.ReadFromJsonAsync<SessionEnrollmentResponse>(token).ConfigureAwait(false) ?? throw new InvalidDataException("Pairing returned no session.");
                    string secret = enrolled.Token;
                    using (HttpRequestMessage request = Me(secret))
                    using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        RequestContext context = await response.Content.ReadFromJsonAsync<RequestContext>(token).ConfigureAwait(false) ?? throw new InvalidDataException("Identity was empty.");
                        if (context.AuthScheme != "XToken" || !context.SessionId.StartsWith("ses_", StringComparison.Ordinal) || context.TenantId != "ten_local") throw new InvalidDataException("Alternate session carrier did not resolve its identity.");
                    }
                    foreach (string name in new[] { "x-email", "x-password", "x-access-key", "x-secret-key", "x-signature", "x-date", "x-nonce", "x-session-token", "x-admin-api-key", "x-api-key" })
                    {
                        using (HttpRequestMessage request = Me(secret))
                        {
                            request.Headers.Add(name, "fixture-auth-material");
                            await ExpectAsync(http, request, HttpStatusCode.Unauthorized, token).ConfigureAwait(false);
                        }
                    }
                    using (HttpRequestMessage request = Me(secret))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer fixture-different-session");
                        await ExpectAsync(http, request, HttpStatusCode.Unauthorized, token).ConfigureAwait(false);
                    }
                    using (HttpRequestMessage request = Me(secret))
                    {
                        request.Headers.TryAddWithoutValidation("x-token", "fixture-duplicate-session");
                        await ExpectAsync(http, request, HttpStatusCode.Unauthorized, token).ConfigureAwait(false);
                    }
                    foreach (string name in new[] { "x-tenant-guid", "x-tenant-id" })
                    {
                        using (HttpRequestMessage request = Me(secret))
                        {
                            request.Headers.Add(name, "ten_other");
                            await ExpectAsync(http, request, HttpStatusCode.Forbidden, token).ConfigureAwait(false);
                        }
                    }
                    using (HttpRequestMessage request = Me(secret))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + secret);
                        request.Headers.Add("x-tenant-guid", "ten_local"); request.Headers.Add("x-tenant-id", "ten_local");
                        await ExpectAsync(http, request, HttpStatusCode.OK, token).ConfigureAwait(false);
                    }
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "v1.0/api/auth/logout"))
                    {
                        request.Headers.Add("x-token", secret);
                        await ExpectAsync(http, request, HttpStatusCode.OK, token).ConfigureAwait(false);
                    }
                    using (HttpRequestMessage request = Me(secret)) await ExpectAsync(http, request, HttpStatusCode.Unauthorized, token).ConfigureAwait(false);
                }
            }
        }
        private static HttpRequestMessage Me(string secret)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "v1.0/api/auth/me"); request.Headers.Add("x-token", secret); return request;
        }
        private static async Task ExpectAsync(HttpClient http, HttpRequestMessage request, HttpStatusCode expected, CancellationToken token)
        {
            using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false))
                if (response.StatusCode != expected) throw new InvalidDataException("Authentication carrier check returned " + (int)response.StatusCode + " instead of " + (int)expected + ".");
        }
    }
}
