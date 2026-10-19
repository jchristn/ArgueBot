namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Tenant role administration contract.</summary>
    public sealed class RoleRecord
    {
        /// <summary>Stable role identifier.</summary>
        public string Id { get; set; } = "";
        /// <summary>Owning tenant, or null for globally visible built-ins.</summary>
        public string? TenantId { get; set; } = null;
        /// <summary>Role display name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Definition is supplied by the platform.</summary>
        public bool Builtin { get; set; } = false;
        /// <summary>Normal administration cannot mutate this definition.</summary>
        public bool Protected { get; set; } = false;
        /// <summary>Whether assignments can use this definition.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Optimistic concurrency revision.</summary>
        public int Revision { get; set; } = 1;
        /// <summary>Creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = default;
        /// <summary>Last update timestamp.</summary>
        public DateTime UpdatedUtc { get; set; } = default;
    }
}
