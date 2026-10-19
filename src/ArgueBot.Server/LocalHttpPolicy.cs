namespace ArgueBot.Server
{
    using System;
    using System.Threading.Tasks;
    using ArgueBot.Core.Services;
    using WatsonWebserver.Core;

    internal static class LocalHttpPolicy
    {
        internal static Task ValidateAsync(HttpContextBase http, int port, bool tls)
        {
            if (http.Request.Url.Host != "127.0.0.1") throw new DomainException(403, "invalid_host", "Use the configured loopback host.");
            string origin = http.Request.Headers["Origin"] ?? "";
            if (origin.Length > 0 && origin != (tls ? "https" : "http") + "://127.0.0.1:" + port && (tls || origin != "http://127.0.0.1:5173")) throw new DomainException(403, "invalid_origin", "Origin is not in the local allowlist.");
            if (origin.Length > 0) http.Response.Headers["Access-Control-Allow-Origin"] = origin;
            return Task.CompletedTask;
        }
    }
}
