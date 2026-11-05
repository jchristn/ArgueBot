namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Create or update a tenant.</summary>
    public sealed class TenantRequest
    {
        private string _Name = "";
        /// <summary>Display name, 1 through 200 characters.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>False disables the tenant.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Expected revision when updating.</summary>
        public long Revision { get; set; }
    }
}
