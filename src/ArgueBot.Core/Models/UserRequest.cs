namespace ArgueBot.Core.Models
{
    /// <summary>Tenant identity creation or complete metadata replacement.</summary>
    public sealed class UserRequest
    {
        /// <summary>Display name, one through two hundred characters.</summary>
        public string Name { get; set; } = "";
        /// <summary>Email address for tenant-bound login.</summary>
        public string Email { get; set; } = "";
        /// <summary>Compatibility domain role: viewer, operator or admin.</summary>
        public string Role { get; set; } = "viewer";
        /// <summary>System administrator flag, assignable only by a system administrator.</summary>
        public bool IsAdmin { get; set; }
        /// <summary>Tenant administrator flag.</summary>
        public bool IsTenantAdmin { get; set; }
        /// <summary>Enable authentication; disabling invalidates existing sessions.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Expected revision for updates; zero for creation.</summary>
        public int ExpectedRevision { get; set; }
        /// <summary>Optional new SHA-256 password-equivalent hexadecimal material. Omit to preserve the password. Never returned.</summary>
        public string? PasswordSha256 { get; set; }
    }
}
