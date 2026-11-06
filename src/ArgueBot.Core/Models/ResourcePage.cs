namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A page of resource summaries ordered by name, with the exact total.</summary>
    public sealed class ResourcePage
    {
        private List<ResourceSummary> _Items = new List<ResourceSummary>();

        /// <summary>Resources on this page.</summary>
        public List<ResourceSummary> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }

        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Resources per page, 1 through 200.</summary>
        public int PageSize { get; set; } = 50;

        /// <summary>Resources matching the filter.</summary>
        public long TotalCount { get; set; }
    }
}
