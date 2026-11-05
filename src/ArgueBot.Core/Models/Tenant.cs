namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A tenant: an isolated workspace with its own users, conversations and configuration.</summary>
    public sealed class Tenant
    {
        private string _Id = "";
        /// <summary>Tenant identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Display name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>False disables sign-in and every token in the tenant.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Protected tenants cannot be deleted or disabled.</summary>
        public bool Protected { get; set; }

        /// <summary>Optimistic revision.</summary>
        public long Revision { get; set; }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Last change time in UTC.</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
