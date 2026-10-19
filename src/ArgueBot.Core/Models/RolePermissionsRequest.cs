namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Scoped authorization administration contract.</summary>
    public sealed class RolePermissionsRequest
    {
        /// <summary>Complete desired active mapping set, maximum 128 distinct visible permission IDs.</summary>
        public List<string> PermissionIds { get; set; } = new List<string>();
        /// <summary>Current role revision required for replacement.</summary>
        public int ExpectedRevision { get; set; } = 0;
    }
}
