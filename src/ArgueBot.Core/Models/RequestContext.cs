namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>RequestContext transport and domain data.</summary>
    public sealed class RequestContext
    {
        /// <summary>Resolved tenant.</summary>
        public string TenantId { get; set; } = "";

        /// <summary>Authenticated user.</summary>
        public string UserId { get; set; } = "";

        /// <summary>System administrator flag.</summary>
        public bool IsAdmin { get; set; }

        /// <summary>Tenant administrator flag.</summary>
        public bool IsTenantAdmin { get; set; }

        /// <summary>Whether credentials are valid.</summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>Effective local role.</summary>
        public string Role { get; set; } = "viewer";

        /// <summary>Request correlation identifier.</summary>
        public string CorrelationId { get; set; } = "";

    }
}
