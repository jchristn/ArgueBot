namespace ArgueBot.Core.Models
{
    /// <summary>Scoped authorization administration contract.</summary>
    public sealed class RolePermissionRecord
    {
        /// <summary>Stable mapping identifier.</summary>
        public string Id { get; set; } = "";
        /// <summary>Role definition identifier.</summary>
        public string RoleId { get; set; } = "";
        /// <summary>Referenced permission identifier.</summary>
        public string PermissionId { get; set; } = "";
        /// <summary>Whether the mapping is active.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Built-in baseline mappings are protected.</summary>
        public bool Protected { get; set; } = false;
        /// <summary>Current referenced permission metadata; inactive permissions confer no grant.</summary>
        public PermissionRecord Permission { get; set; } = new PermissionRecord();
    }
}
