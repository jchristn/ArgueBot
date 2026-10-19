namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Scoped authorization administration contract.</summary>
    public sealed class PermissionRecord
    {
        /// <summary>Stable permission identifier.</summary>
        public string Id { get; set; } = "";
        /// <summary>Owning tenant or null for protected global definitions.</summary>
        public string? TenantId { get; set; } = null;
        /// <summary>Human-readable permission name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Resource types or All wildcard, bounded to 32 entries.</summary>
        public List<string> ResourceTypes { get; set; } = new List<string>();
        /// <summary>Read/Create/Update/Delete/Write/Execute/Admin or All, bounded to 16 entries.</summary>
        public List<string> Operations { get; set; } = new List<string>();
        /// <summary>Permit or Deny; explicit matching deny wins.</summary>
        public string Effect { get; set; } = "Permit";
        /// <summary>Narrow grants to actual resource ownership.</summary>
        public bool RequiresOwnership { get; set; } = false;
        /// <summary>Whether the permission can contribute authority.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Global seeded permissions cannot be edited through normal administration.</summary>
        public bool Protected { get; set; } = false;
        /// <summary>Optimistic concurrency revision.</summary>
        public int Revision { get; set; } = 1;
        /// <summary>Creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = default;
        /// <summary>Last update timestamp.</summary>
        public DateTime UpdatedUtc { get; set; } = default;
    }
}
