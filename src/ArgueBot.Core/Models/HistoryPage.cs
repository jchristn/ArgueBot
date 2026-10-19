namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>HistoryPage operational contract.</summary>
    public sealed class HistoryPage
    {
        private List<RequestHistoryEntry> _Items = new List<RequestHistoryEntry>();

        /// <summary>Matching captures.</summary>
        public List<RequestHistoryEntry> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total matching records.</summary>
        public long TotalCount { get; set; }
    }
}
