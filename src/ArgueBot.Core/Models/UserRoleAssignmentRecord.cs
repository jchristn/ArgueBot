namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Tenant user scoped-role assignment contract.</summary>
    public sealed class UserRoleAssignmentRecord
    {
        /// <summary>Stable assignment identifier.</summary>
        public string Id { get; set; } = "";
        /// <summary>Authenticated tenant binding.</summary>
        public string TenantId { get; set; } = "";
        /// <summary>Assigned tenant user.</summary>
        public string UserId { get; set; } = "";
        /// <summary>Optional role GUID; an existing inactive reference blocks fallback.</summary>
        public string RoleId { get; set; } = "";
        /// <summary>Optional fallback role name when its GUID is absent.</summary>
        public string RoleName { get; set; } = "";
        /// <summary>Tenant or Resource scope.</summary>
        public string ResourceScope { get; set; } = "Tenant";
        /// <summary>Exact opaque target ID for Resource scope; empty for Tenant scope.</summary>
        public string ResourceId { get; set; } = "";
        /// <summary>Tenant scope reaches child resources only when explicitly enabled.</summary>
        public bool InheritsToChildren { get; set; } = false;
        /// <summary>Whether this assignment participates in resolution.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Protected assignments cannot be changed through normal administration.</summary>
        public bool Protected { get; set; } = false;
        /// <summary>Optimistic concurrency revision.</summary>
        public int Revision { get; set; } = 1;
        /// <summary>Creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = default;
        /// <summary>Last update timestamp.</summary>
        public DateTime UpdatedUtc { get; set; } = default;
    }
}
