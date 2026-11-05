namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Audit search filters. Empty values match everything.</summary>
    public sealed class AuditQuery
    {
        private string _Operation = "";
        /// <summary>Exact event type, for example AuthFailure.</summary>
        public string Operation { get { return _Operation; } set { ArgumentNullException.ThrowIfNull(value); _Operation = value; } }

        private string _Outcome = "";
        /// <summary>success, failure or denied.</summary>
        public string Outcome { get { return _Outcome; } set { ArgumentNullException.ThrowIfNull(value); _Outcome = value; } }

        private string _UserId = "";
        /// <summary>User ID.</summary>
        public string UserId { get { return _UserId; } set { ArgumentNullException.ThrowIfNull(value); _UserId = value; } }

        private string _TenantId = "";
        /// <summary>Tenant ID; system administrators only. Tenant administrators always see their own tenant.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        /// <summary>Earliest record time.</summary>
        public DateTime? FromUtc { get; set; }

        /// <summary>Latest record time.</summary>
        public DateTime? ToUtc { get; set; }

        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Records per page, 1 through 200.</summary>
        public int PageSize { get; set; } = 50;
    }
}
