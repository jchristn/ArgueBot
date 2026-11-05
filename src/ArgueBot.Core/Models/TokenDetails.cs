namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>The identity behind the current bearer token.</summary>
    public sealed class TokenDetails
    {
        private string _PrincipalType = "";
        /// <summary>User for a session, Credential for a bearer-token credential.</summary>
        public string PrincipalType { get { return _PrincipalType; } set { ArgumentNullException.ThrowIfNull(value); _PrincipalType = value; } }

        private string _AuthScheme = "";
        /// <summary>Bearer or XToken.</summary>
        public string AuthScheme { get { return _AuthScheme; } set { ArgumentNullException.ThrowIfNull(value); _AuthScheme = value; } }

        private string _SessionId = "";
        /// <summary>Session ID, or empty for a credential token.</summary>
        public string SessionId { get { return _SessionId; } set { ArgumentNullException.ThrowIfNull(value); _SessionId = value; } }

        private string _CredentialId = "";
        /// <summary>Credential ID, or empty for a session.</summary>
        public string CredentialId { get { return _CredentialId; } set { ArgumentNullException.ThrowIfNull(value); _CredentialId = value; } }

        /// <summary>When the token stops working, if it expires.</summary>
        public DateTime? ExpiresUtc { get; set; }

        private User _User = new User();
        /// <summary>Signed-in user.</summary>
        public User User { get { return _User; } set { ArgumentNullException.ThrowIfNull(value); _User = value; } }

        private Tenant _Tenant = new Tenant();
        /// <summary>Tenant the token is bound to.</summary>
        public Tenant Tenant { get { return _Tenant; } set { ArgumentNullException.ThrowIfNull(value); _Tenant = value; } }
    }
}
