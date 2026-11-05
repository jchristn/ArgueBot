namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Public sign-in page information.</summary>
    public sealed class LoginInfo
    {
        private string _Version = "";
        /// <summary>Product version.</summary>
        public string Version { get { return _Version; } set { ArgumentNullException.ThrowIfNull(value); _Version = value; } }

        private string _DefaultEmail = "";
        /// <summary>Default administrator email.</summary>
        public string DefaultEmail { get { return _DefaultEmail; } set { ArgumentNullException.ThrowIfNull(value); _DefaultEmail = value; } }

        private string _DefaultPassword = "";
        /// <summary>Default administrator password.</summary>
        public string DefaultPassword { get { return _DefaultPassword; } set { ArgumentNullException.ThrowIfNull(value); _DefaultPassword = value; } }

        private string _DefaultToken = "";
        /// <summary>Default administrator bearer token.</summary>
        public string DefaultToken { get { return _DefaultToken; } set { ArgumentNullException.ThrowIfNull(value); _DefaultToken = value; } }

        /// <summary>True when more than one active tenant exists, so the page may need to ask for one.</summary>
        public bool MultipleTenants { get; set; }
    }
}
