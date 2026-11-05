namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>409 tenant_required: the email and password match accounts in several tenants; choose one and sign in again with tenantId.</summary>
    public sealed class TenantRequiredError
    {
        private string _Code = "tenant_required";
        /// <summary>Always tenant_required.</summary>
        public string Code { get { return _Code; } set { ArgumentNullException.ThrowIfNull(value); _Code = value; } }

        private string _Message = "";
        /// <summary>Human-readable explanation.</summary>
        public string Message { get { return _Message; } set { ArgumentNullException.ThrowIfNull(value); _Message = value; } }

        private List<TenantChoice> _Tenants = new List<TenantChoice>();
        /// <summary>Tenants whose account accepted the password.</summary>
        public List<TenantChoice> Tenants { get { return _Tenants; } set { ArgumentNullException.ThrowIfNull(value); _Tenants = value; } }
    }
}
