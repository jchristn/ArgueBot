namespace ArgueBot.Core.Models
{
    using System;
    using ArgueBot.Core.Configuration;

    /// <summary>Owner-readable attachment metadata. It contains no session or pairing secret.</summary>
    public sealed class LocalServiceDescriptor
    {
        private string _HomeId = "";
        private string _Endpoint = "";
        private string _InstanceId = "";

        /// <summary>Opaque identifier matching this home's seed and canonical path, verified against listener readiness.</summary>
        public string HomeId { get { return _HomeId; } set { ArgumentNullException.ThrowIfNull(value); _HomeId = value; } }
        /// <summary>Local listener address.</summary>
        public string Endpoint { get { return _Endpoint; } set { ArgumentNullException.ThrowIfNull(value); _Endpoint = value; } }
        /// <summary>Public TLS certificate SHA-256 pin for this exact home endpoint, or empty for HTTP.</summary>
        public string CertificateSha256 { get; set; } = "";
        /// <summary>Attachment protocol, independent of product and schema versions.</summary>
        public int Protocol { get; set; } = ServiceProtocol.CurrentVersion;
        /// <summary>Process instance identifier used when removing stale metadata.</summary>
        public string InstanceId { get { return _InstanceId; } set { ArgumentNullException.ThrowIfNull(value); _InstanceId = value; } }
        /// <summary>Operating-system process ID for diagnostics, never a substitute for the ownership lock.</summary>
        public int ProcessId { get; set; }
        /// <summary>Service startup time.</summary>
        public DateTime StartedUtc { get; set; }
    }
}
