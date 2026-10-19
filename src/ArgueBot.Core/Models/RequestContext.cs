namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>RequestContext transport and domain data.</summary>
    public sealed class RequestContext
    {
        /// <summary>UTC request receipt time.</summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Monotonic timestamp captured before route execution.</summary>
        public long StartedTimestamp { get; set; }

        private string _TenantId = "";
        private string _UserId = "";
        private string _Role = "viewer";
        private string _CorrelationId = "";

        private string _SessionId = "";
        private string _AuthScheme = "";
        /// <summary>Resolved revocable session identifier; empty for non-session internal calls.</summary>
        public string SessionId { get { return _SessionId; } set { ArgumentNullException.ThrowIfNull(value); _SessionId = value; } }
        /// <summary>Validated transport carrier: BearerToken or XToken for enrolled HTTP sessions.</summary>
        public string AuthScheme { get { return _AuthScheme; } set { ArgumentNullException.ThrowIfNull(value); _AuthScheme = value; } }

        /// <summary>Resolved tenant.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        /// <summary>Authenticated user.</summary>
        public string UserId { get { return _UserId; } set { ArgumentNullException.ThrowIfNull(value); _UserId = value; } }

        /// <summary>System administrator flag.</summary>
        public bool IsAdmin { get; set; }

        /// <summary>Tenant administrator flag.</summary>
        public bool IsTenantAdmin { get; set; }

        /// <summary>Whether credentials are valid.</summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>Effective local role.</summary>
        public string Role { get { return _Role; } set { ArgumentNullException.ThrowIfNull(value); _Role = value; } }

        /// <summary>Request correlation identifier.</summary>
        public string CorrelationId { get { return _CorrelationId; } set { ArgumentNullException.ThrowIfNull(value); _CorrelationId = value; } }

    }
}
