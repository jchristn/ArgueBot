namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Append-only security accounting without request content or secret material.</summary>
    public sealed class AuditEntry
    {
        private string _Id = "";
        /// <summary>Immutable event identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        /// <summary>Resolved tenant, or null for unauthenticated/system events.</summary>
        public string? TenantId { get; set; } = null;
        /// <summary>Validated actor, never an untrusted request hint.</summary>
        public string? UserId { get; set; } = null;
        private string _Operation = "";
        /// <summary>Bounded semantic security operation.</summary>
        public string Operation { get { return _Operation; } set { ArgumentNullException.ThrowIfNull(value); _Operation = value; } }
        private string _Outcome = "";
        /// <summary>Semantic result without credentials or request content.</summary>
        public string Outcome { get { return _Outcome; } set { ArgumentNullException.ThrowIfNull(value); _Outcome = value; } }
        private string _SessionId = "";
        /// <summary>Session identifier where known; never the token.</summary>
        public string SessionId { get { return _SessionId; } set { ArgumentNullException.ThrowIfNull(value); _SessionId = value; } }
        private string _AuthScheme = "";
        /// <summary>Validated authentication carrier where known.</summary>
        public string AuthScheme { get { return _AuthScheme; } set { ArgumentNullException.ThrowIfNull(value); _AuthScheme = value; } }
        private string _CorrelationId = "";
        /// <summary>Request correlation identifier where available.</summary>
        public string CorrelationId { get { return _CorrelationId; } set { ArgumentNullException.ThrowIfNull(value); _CorrelationId = value; } }
        private string _ResourceType = "";
        /// <summary>Semantic resource type.</summary>
        public string ResourceType { get { return _ResourceType; } set { ArgumentNullException.ThrowIfNull(value); _ResourceType = value; } }
        private string _ResourceId = "";
        /// <summary>Target identifier where known.</summary>
        public string ResourceId { get { return _ResourceId; } set { ArgumentNullException.ThrowIfNull(value); _ResourceId = value; } }
        private string _ErrorCode = "";
        /// <summary>Safe failure code, empty on success.</summary>
        public string ErrorCode { get { return _ErrorCode; } set { ArgumentNullException.ThrowIfNull(value); _ErrorCode = value; } }
        /// <summary>Event timestamp in UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
