namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;
    using System;

    /// <summary>Server-filtered recovery artifact page.</summary>
    public sealed class BackupPage
    {
        private List<BackupEntry> _Items = new List<BackupEntry>();

        /// <summary>Matching items in stable newest-first order.</summary>
        public List<BackupEntry> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }
        /// <summary>Matching total independent of page size.</summary>
        public long TotalCount { get; set; }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; }
        /// <summary>Items per page.</summary>
        public int PageSize { get; set; }
    }
}
