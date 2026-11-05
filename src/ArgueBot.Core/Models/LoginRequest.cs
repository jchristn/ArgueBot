namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Email and password sign-in. The same values may be sent as x-email, x-password and x-tenant-id headers.</summary>
    public sealed class LoginRequest
    {
        private string _Email = "";
        /// <summary>Account email.</summary>
        public string Email { get { return _Email; } set { ArgumentNullException.ThrowIfNull(value); _Email = value; } }

        private string _Password = "";
        /// <summary>Account password.</summary>
        public string Password { get { return _Password; } set { ArgumentNullException.ThrowIfNull(value); _Password = value; } }

        private string _TenantId = "";
        /// <summary>Tenant to sign in to; needed only when the email exists in several tenants.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }
    }
}
