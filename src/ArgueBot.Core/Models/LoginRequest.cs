namespace ArgueBot.Core.Models
{
    /// <summary>Tenant-bound password-equivalent exchange. All fields are sensitive authentication input.</summary>
    public sealed class LoginRequest
    {
        /// <summary>Explicit tenant identifier.</summary>
        public string TenantId { get; set; } = "";
        /// <summary>Tenant-local email address.</summary>
        public string Email { get; set; } = "";
        /// <summary>Sixty-four hexadecimal SHA-256 password-equivalent characters. Treat this value as a password.</summary>
        public string PasswordSha256 { get; set; } = "";
    }
}
