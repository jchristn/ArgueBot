namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Host names the service answers to. 127.0.0.1 and localhost are always allowed. A <c>*</c> entry allows any host name,
    /// for services reached by names that are not known in advance; sign-in still protects every non-public route.
    /// </summary>
    public sealed class HostAllowlist
    {
        /// <summary>The entry that allows any host name.</summary>
        public const string Any = "*";
        private readonly HashSet<string> _Hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost" };

        /// <summary>Build the list from configured names (settings and the comma-separated environment value).</summary>
        public HostAllowlist(IEnumerable<string> configured)
        {
            ArgumentNullException.ThrowIfNull(configured);
            foreach (string host in configured.Select(item => (item ?? "").Trim()).Where(item => item.Length > 0)) _Hosts.Add(host);
        }

        /// <summary>True when any host name is accepted.</summary>
        public bool AllowsAny { get { return _Hosts.Contains(Any); } }

        /// <summary>The configured names, including the loopback defaults.</summary>
        public IReadOnlyCollection<string> Hosts { get { return _Hosts; } }

        /// <summary>True when a request for this host name (without a port) is accepted.</summary>
        public bool Allows(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.Length > 0 && (AllowsAny || _Hosts.Contains(name));
        }

        /// <summary>True for 127.0.0.1 and localhost.</summary>
        public static bool IsLoopback(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name == "127.0.0.1" || name.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }
    }
}
