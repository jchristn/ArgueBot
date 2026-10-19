namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Scoped authorization administration contract.</summary>
    public sealed class PermissionRequest
    {
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
        /// <summary>Current revision required for an update.</summary>
        public int ExpectedRevision { get; set; } = 0;
    }
}
