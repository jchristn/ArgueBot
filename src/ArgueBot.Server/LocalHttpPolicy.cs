namespace ArgueBot.Server
{
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;

    /// <summary>
    /// Host and origin allowlist. 127.0.0.1 and localhost are always allowed; other names come from the AllowedHosts setting
    /// and the comma-separated ARGUEBOT_ALLOWED_HOSTS environment variable, where * allows any name. Browser origins must match the requested host.
    /// </summary>
    internal sealed class LocalHttpPolicy
    {
        private static readonly Regex Authority = new Regex(@"\A(\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9.-]+)(?::([0-9]{1,5}))?\z", RegexOptions.CultureInvariant);
        private readonly HostAllowlist _Hosts;
        private readonly int _Port;
        private readonly bool _Tls;
        private readonly bool _MappedPort;

        internal LocalHttpPolicy(int port, bool tls, bool mappedPort, IEnumerable<string> allowedHosts)
        {
            _Port = port; _Tls = tls; _MappedPort = mappedPort;
            _Hosts = new HostAllowlist(allowedHosts.Concat((Environment.GetEnvironmentVariable("ARGUEBOT_ALLOWED_HOSTS") ?? "").Split(',')));
        }

        /// <summary>Allowed host names, for diagnostics.</summary>
        internal HostAllowlist Hosts { get { return _Hosts; } }

        internal Task ValidateAsync(HttpContextBase http)
        {
            // Watson composes Url.Host from the listener binding. Inspect the actual request authority.
            string host = http.Request.Headers["Host"] ?? "";
            Match authority = Authority.Match(host);
            string name = authority.Success ? authority.Groups[1].Value : "";
            bool loopback = HostAllowlist.IsLoopback(name);
            int requestedPort = authority.Success && authority.Groups[2].Success ? int.Parse(authority.Groups[2].Value, CultureInfo.InvariantCulture) : _Tls ? 443 : 80;
            if (!authority.Success || !_Hosts.Allows(name) || requestedPort < 1 || requestedPort > 65535 || (loopback && !_MappedPort && requestedPort != _Port))
                throw new DomainException(403, "invalid_host", "This host name is not allowed. Add it to AllowedHosts in the service settings or to ARGUEBOT_ALLOWED_HOSTS, then restart the service.");
            string origin = http.Request.Headers["Origin"] ?? "";
            if (origin.Length > 0)
            {
                bool parsed = Uri.TryCreate(origin, UriKind.Absolute, out Uri? source) && (source.Scheme == "http" || source.Scheme == "https");
                bool sameHost = parsed && string.Equals(source!.Authority, host, StringComparison.OrdinalIgnoreCase);
                bool developer = !_Tls && (origin == "http://127.0.0.1:5173" || origin == "http://localhost:5173");
                if (!sameHost && !developer) throw new DomainException(403, "invalid_origin", "This browser origin is not allowed.");
                http.Response.Headers["Access-Control-Allow-Origin"] = origin;
                http.Response.Headers["Vary"] = "Origin";
            }
            return Task.CompletedTask;
        }
    }
}
