namespace ArgueBot.Core.Security
{
    /// <summary>Server-resolved authentication facts consumed by permission evaluation, never caller-supplied authorization claims.</summary>
    public sealed class AuthorizationPrincipal
    {
        /// <summary>Native user, credential or administrator identifier.</summary>
        public string Id { get; set; } = "";
        /// <summary>Validated native principal category.</summary>
        public AuthorizationPrincipalKind Kind { get; set; }
        /// <summary>Authenticated tenant. Credentials and ordinary users cannot cross this binding.</summary>
        public string TenantId { get; set; } = "";
        /// <summary>Owning user identifier, required for credential principals.</summary>
        public string OwnerUserId { get; set; } = "";
        /// <summary>Authentication has been verified by the trusted transport or service resolver.</summary>
        public bool IsAuthenticated { get; set; }
        /// <summary>All required backing identities, tenant and credential remain active.</summary>
        public bool IsActive { get; set; }
        /// <summary>System administrator flag. For credentials it describes the owner ceiling only and never grants credential bypass.</summary>
        public bool IsAdmin { get; set; }
        /// <summary>Tenant administrator flag. For credentials it describes the owner ceiling only.</summary>
        public bool IsTenantAdmin { get; set; }
    }
}
