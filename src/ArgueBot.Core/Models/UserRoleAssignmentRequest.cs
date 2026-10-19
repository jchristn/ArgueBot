namespace ArgueBot.Core.Models
{
    /// <summary>Tenant user scoped-role assignment contract.</summary>
    public sealed class UserRoleAssignmentRequest
    {
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
        /// <summary>Current assignment revision required for updates.</summary>
        public int ExpectedRevision { get; set; } = 0;
    }
}
