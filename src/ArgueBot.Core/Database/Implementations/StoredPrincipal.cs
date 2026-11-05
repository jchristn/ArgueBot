namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Models;
    using System;

    /// <summary>A stored session or credential with the state of its user and tenant.</summary>
    internal sealed class StoredPrincipal
    {
        internal User User { get; set; } = new User();
        internal Tenant Tenant { get; set; } = new Tenant();
        internal string Id { get; set; } = "";
        internal string CredentialId { get; set; } = "";
        internal string PrincipalType { get; set; } = "";
        internal string TokenHash { get; set; } = "";
        internal bool Active { get; set; }
        internal bool CredentialActive { get; set; } = true;
        internal DateTime? ExpiresUtc { get; set; }
        internal DateTime? LastUsedUtc { get; set; }
    }
}
