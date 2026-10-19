namespace ArgueBot.Core.Security
{
    using System;

    /// <summary>Platform-controlled encrypted session claims; permission decisions always re-read the database.</summary>
    public sealed class SessionClaims
    {
        /// <summary>Session identifier.</summary>
        public string SessionId { get; set; } = "";
        /// <summary>Tenant scope.</summary>
        public string TenantId { get; set; } = "";
        /// <summary>Principal user identifier.</summary>
        public string UserId { get; set; } = "";
        /// <summary>Principal type, normally User.</summary>
        public string PrincipalType { get; set; } = "User";
        /// <summary>Optional credential principal identifier.</summary>
        public string? CredentialId { get; set; }
        /// <summary>UTC issue timestamp.</summary>
        public DateTime IssuedUtc { get; set; }
        /// <summary>UTC expiry timestamp.</summary>
        public DateTime ExpiresUtc { get; set; }
        /// <summary>Random token nonce independent of its encryption nonce.</summary>
        public string TokenId { get; set; } = "";
    }
}
