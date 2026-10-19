namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Scoped authorization administration contract.</summary>
    public sealed class RolePermissionPage
    {
        /// <summary>Bounded ordered mappings and current permission definitions.</summary>
        public List<RolePermissionRecord> Items { get; set; } = new List<RolePermissionRecord>();
        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Bounded page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total matching mappings.</summary>
        public long TotalCount { get; set; } = 0;
        /// <summary>Role revision observed with this mapping page.</summary>
        public int RoleRevision { get; set; } = 0;
    }
}
