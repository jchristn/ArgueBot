namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A person who signs in to one tenant. Password material is never returned.</summary>
    public sealed class User
    {
        private string _Id = "";
        /// <summary>User identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _TenantId = "";
        /// <summary>Owning tenant.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        private string _FirstName = "";
        /// <summary>First name.</summary>
        public string FirstName { get { return _FirstName; } set { ArgumentNullException.ThrowIfNull(value); _FirstName = value; } }

        private string _LastName = "";
        /// <summary>Last name.</summary>
        public string LastName { get { return _LastName; } set { ArgumentNullException.ThrowIfNull(value); _LastName = value; } }

        private string _Email = "";
        /// <summary>Email address, unique within the tenant.</summary>
        public string Email { get { return _Email; } set { ArgumentNullException.ThrowIfNull(value); _Email = value; } }

        /// <summary>System administrator: manages every tenant.</summary>
        public bool IsAdmin { get; set; }

        /// <summary>Tenant administrator: manages users and credentials in this tenant.</summary>
        public bool IsTenantAdmin { get; set; }

        /// <summary>False blocks sign-in and revokes access.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Protected users cannot be deleted or disabled.</summary>
        public bool Protected { get; set; }

        /// <summary>Optimistic revision.</summary>
        public long Revision { get; set; }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Last change time in UTC.</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
