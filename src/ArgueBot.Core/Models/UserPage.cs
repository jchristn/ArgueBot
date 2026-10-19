namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Bounded tenant identity search results.</summary>
    public sealed class UserPage
    {
        /// <summary>Ordered public identity metadata.</summary>
        public List<UserRecord> Items { get; set; } = new List<UserRecord>();
        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; }
        /// <summary>Maximum results per page.</summary>
        public int PageSize { get; set; }
        /// <summary>Count matching the tenant and search filter.</summary>
        public long TotalCount { get; set; }
    }
}
