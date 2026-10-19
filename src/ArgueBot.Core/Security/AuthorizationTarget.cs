namespace ArgueBot.Core.Security
{
    /// <summary>Statically mapped operation and server-resolved authorization scope, independent of HTTP verb guesses.</summary>
    public sealed class AuthorizationTarget
    {
        /// <summary>Explicit target tenant for tenant-scoped operations.</summary>
        public string TenantId { get; set; } = "";
        /// <summary>Required resource type, such as Conversation, Run, Participant or User.</summary>
        public string ResourceType { get; set; } = "";
        /// <summary>Read, Create, Update, Delete, Execute, Admin or the aggregate Write.</summary>
        public string Operation { get; set; } = "";
        /// <summary>Exact authorization scope identifier. Run operations can deliberately map to their parent conversation scope.</summary>
        public string ResourceId { get; set; } = "";
        /// <summary>Actual stored resource owner, resolved by the service rather than trusted from a request body.</summary>
        public string OwnerUserId { get; set; } = "";
        /// <summary>Platform-wide control plane. Tenant grants cannot authorize this scope.</summary>
        public bool IsPlatform { get; set; }
    }
}
