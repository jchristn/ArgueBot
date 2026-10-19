namespace ArgueBot.Core.Models
{
    /// <summary>Tenant role administration contract.</summary>
    public sealed class RoleRequest
    {
        /// <summary>Display name for a tenant custom role.</summary>
        public string Name { get; set; } = "";
        /// <summary>Whether the custom role can contribute authority.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Current revision required for updates.</summary>
        public int ExpectedRevision { get; set; } = 0;
        /// <summary>Optional accessible role to copy when creating a custom role; omit for updates.</summary>
        public string CloneRoleId { get; set; } = "";
    }
}
