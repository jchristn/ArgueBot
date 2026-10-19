namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Public tenant-bound identity metadata. Password material is never returned.</summary>
    public sealed class UserRecord
    {
        /// <summary>Stable user identifier.</summary>
        public string Id { get; set; } = "";
        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = "";
        /// <summary>Display name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Normalized email, unique within the tenant when configured.</summary>
        public string Email { get; set; } = "";
        /// <summary>Compatibility domain role: viewer, operator or admin.</summary>
        public string Role { get; set; } = "viewer";
        /// <summary>System administrator bypass.</summary>
        public bool IsAdmin { get; set; }
        /// <summary>Tenant administrator bypass.</summary>
        public bool IsTenantAdmin { get; set; }
        /// <summary>Whether authentication remains enabled.</summary>
        public bool Active { get; set; }
        /// <summary>Whether normal management must refuse mutation.</summary>
        public bool Protected { get; set; }
        /// <summary>Whether password login has been configured.</summary>
        public bool HasPassword { get; set; }
        /// <summary>Optimistic mutation revision.</summary>
        public int Revision { get; set; }
        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>UTC last update timestamp.</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
