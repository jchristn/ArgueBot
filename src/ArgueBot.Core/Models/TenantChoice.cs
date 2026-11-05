namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A tenant offered at sign-in when the email and password match accounts in several tenants.</summary>
    public sealed class TenantChoice
    {
        private string _Id = "";
        /// <summary>Tenant ID to send as tenantId.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Tenant name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }
    }
}
