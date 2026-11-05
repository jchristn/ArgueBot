namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System.Collections.Generic;

    /// <summary>409 tenant_required: the email and password match accounts in several tenants.</summary>
    public sealed class TenantRequiredException : DomainException
    {
        /// <summary>Create the exception with the tenants whose account accepted the password.</summary>
        public TenantRequiredException(List<TenantChoice> tenants) : base(409, "tenant_required", "This email belongs to several tenants. Choose one and sign in again.", "tenantId") { Tenants = tenants; }
        /// <summary>Tenants to choose from.</summary>
        public List<TenantChoice> Tenants { get; }
    }
}
