namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A newest-first page of audit records with the exact total.</summary>
    public sealed class AuditPage
    {
        private List<AuditEvent> _Items = new List<AuditEvent>();
        /// <summary>Records on this page.</summary>
        public List<AuditEvent> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }

        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Records per page, 1 through 200.</summary>
        public int PageSize { get; set; } = 50;

        /// <summary>Records matching the filter.</summary>
        public long TotalCount { get; set; }
    }
}
