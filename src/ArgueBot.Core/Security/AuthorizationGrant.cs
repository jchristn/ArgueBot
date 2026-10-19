namespace ArgueBot.Core.Security
{
    using System.Collections.Generic;

    /// <summary>One expanded permission with its assignment scope. The store resolver must combine role, mapping, permission and assignment activity.</summary>
    public sealed class AuthorizationGrant
    {
        /// <summary>Stable provenance identifier for the expanded grant.</summary>
        public string Id { get; set; } = "";
        /// <summary>Assignment tenant, including grants expanded from globally visible built-in role definitions.</summary>
        public string TenantId { get; set; } = "";
        /// <summary>Supported resource types, or All as a wildcard.</summary>
        public List<string> ResourceTypes { get; set; } = new List<string>();
        /// <summary>Supported operations, including Write expansion and All wildcard.</summary>
        public List<string> Operations { get; set; } = new List<string>();
        /// <summary>Permit or explicit deny.</summary>
        public AuthorizationEffect Effect { get; set; }
        /// <summary>Tenant-wide or exact-resource assignment.</summary>
        public AuthorizationScope Scope { get; set; }
        /// <summary>Exact identifier required for a resource-scoped grant.</summary>
        public string ResourceId { get; set; } = "";
        /// <summary>Whether a tenant assignment reaches child resources and their collection operations.</summary>
        public bool InheritsToChildren { get; set; }
        /// <summary>Optional narrowing condition for compatibility grants that apply only to owned data.</summary>
        public bool RequiresOwnership { get; set; }
        /// <summary>All contributing stored records remain active.</summary>
        public bool Active { get; set; } = true;
    }
}
