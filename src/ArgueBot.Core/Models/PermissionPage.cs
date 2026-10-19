namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Scoped authorization administration contract.</summary>
    public sealed class PermissionPage
    {
        /// <summary>Ordered permission metadata.</summary>
        public List<PermissionRecord> Items { get; set; } = new List<PermissionRecord>();
        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Bounded page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total matching visible permissions.</summary>
        public long TotalCount { get; set; } = 0;
    }
}
