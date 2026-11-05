namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Nonexecuting preview of a saved endpoint's request destinations and binding names.</summary>
    public sealed class EndpointPreview
    {
        private string _EndpointId = "";
        /// <summary>Saved endpoint identity.</summary>
        public string EndpointId { get { return _EndpointId; } set { ArgumentNullException.ThrowIfNull(value); _EndpointId = value; } }
        /// <summary>Configuration revision described by this preview.</summary>
        public long Revision { get; set; }
        private string _GenerationUrl = "";
        /// <summary>Normalized destination for POST generation requests, before query bindings are added.</summary>
        public string GenerationUrl { get { return _GenerationUrl; } set { ArgumentNullException.ThrowIfNull(value); _GenerationUrl = value; } }
        private string _DiscoveryUrl = "";
        /// <summary>Normalized destination for GET discovery requests, before query bindings are added.</summary>
        public string DiscoveryUrl { get { return _DiscoveryUrl; } set { ArgumentNullException.ThrowIfNull(value); _DiscoveryUrl = value; } }
        private List<string> _HeaderNames = new List<string>();
        /// <summary>Configured outbound header names; every value is omitted, including nonsecret routing values.</summary>
        public List<string> HeaderNames { get { return _HeaderNames; } set { ArgumentNullException.ThrowIfNull(value); _HeaderNames = value; } }
        private List<string> _QueryNames = new List<string>();
        /// <summary>Configured query parameter names; credentials are neither resolved nor returned.</summary>
        public List<string> QueryNames { get { return _QueryNames; } set { ArgumentNullException.ThrowIfNull(value); _QueryNames = value; } }
    }
}
