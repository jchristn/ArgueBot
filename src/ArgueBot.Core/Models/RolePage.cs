namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Tenant role administration contract.</summary>
    public sealed class RolePage
    {
        /// <summary>Ordered role metadata, including visible built-ins.</summary>
        public List<RoleRecord> Items { get; set; } = new List<RoleRecord>();
        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Requested bounded page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total matching role definitions.</summary>
        public long TotalCount { get; set; } = 0;
    }
}
