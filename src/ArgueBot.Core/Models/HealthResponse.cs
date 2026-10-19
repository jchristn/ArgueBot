namespace ArgueBot.Core.Models
{
    using System;
    /// <summary>Anonymous readiness and opaque home binding, without user identity or private filesystem paths.</summary>
    public sealed class HealthResponse
    {
        private string _HomeProof = "";
        private string _HomeId = "";
        private string _Status = "";

        /// <summary>Opaque path-bound application-home routing identifier. Not an authentication credential.</summary>
        public string HomeId { get { return _HomeId; } set { ArgumentNullException.ThrowIfNull(value); _HomeId = value; } }
        /// <summary>HMAC-SHA256 process proof bound to an optional random challenge, listener port and home. Empty when no challenge was requested.</summary>
        public string HomeProof { get { return _HomeProof; } set { ArgumentNullException.ThrowIfNull(value); _HomeProof = value; } }
        /// <summary>Readiness state.</summary>
        public string Status { get { return _Status; } set { ArgumentNullException.ThrowIfNull(value); _Status = value; } }
        /// <summary>Current database schema version, independent of product version.</summary>
        public int Schema { get; set; }
        /// <summary>HTTP attachment protocol version, separate from schema and product versions.</summary>
        public int Protocol { get; set; }

    }
}
