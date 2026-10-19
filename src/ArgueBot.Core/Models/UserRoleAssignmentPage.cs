namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Tenant user scoped-role assignment contract.</summary>
    public sealed class UserRoleAssignmentPage
    {
        /// <summary>Ordered active and inactive assignments for one tenant user.</summary>
        public List<UserRoleAssignmentRecord> Items { get; set; } = new List<UserRoleAssignmentRecord>();
        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Bounded page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total assignments for the selected user.</summary>
        public long TotalCount { get; set; } = 0;
    }
}
