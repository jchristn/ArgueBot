namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Server-filtered recovery operation page.</summary>
    public sealed class BackupOperationPage
    {
        private List<BackupOperation> _Items = new List<BackupOperation>();

        /// <summary>Matching items in stable newest-first order.</summary>
        public List<BackupOperation> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }
        /// <summary>Matching total independent of page size.</summary>
        public long TotalCount { get; set; }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; }
        /// <summary>Items per page.</summary>
        public int PageSize { get; set; }
    }
}
