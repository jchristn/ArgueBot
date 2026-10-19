namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>AuditPage security accounting page.</summary>
    public sealed class AuditPage
    {
        private List<AuditEntry> _Items = new List<AuditEntry>();

        /// <summary>Matching immutable audit events.</summary>
        public List<AuditEntry> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total matching records.</summary>
        public long TotalCount { get; set; }
    }
}
