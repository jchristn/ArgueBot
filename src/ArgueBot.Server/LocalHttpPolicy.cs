namespace ArgueBot.Server
{
    using ArgueBot.Core.Services;
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;

    internal static class LocalHttpPolicy
    {
        internal static Task ValidateAsync(HttpContextBase http, int port, bool tls, bool mappedPort = false)
        {
            // Watson composes Url.Host from the listener binding. Inspect the actual request authority.
            string host = http.Request.Headers["Host"] ?? "";
            Match authority = Regex.Match(host, @"\A127\.0\.0\.1(?::([0-9]{1,5}))?\z", RegexOptions.CultureInvariant);
            int requestedPort = authority.Groups[1].Success ? int.Parse(authority.Groups[1].Value, CultureInfo.InvariantCulture) : tls ? 443 : 80;
            if (!authority.Success || requestedPort < 1 || requestedPort > 65535 || (!mappedPort && requestedPort != port)) throw new DomainException(403, "invalid_host", "Use the configured loopback host.");
            string origin = http.Request.Headers["Origin"] ?? "";
            string expectedOrigin = new Uri((tls ? "https" : "http") + "://" + host).GetLeftPart(UriPartial.Authority);
            if (origin.Length > 0 && origin != expectedOrigin && (tls || origin != "http://127.0.0.1:5173")) throw new DomainException(403, "invalid_origin", "Origin is not in the local allowlist.");
            if (origin.Length > 0) http.Response.Headers["Access-Control-Allow-Origin"] = origin;
            return Task.CompletedTask;
        }
    }
}
