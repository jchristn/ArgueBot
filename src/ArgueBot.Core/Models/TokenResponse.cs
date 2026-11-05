namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A newly issued session token and the identity it represents.</summary>
    public sealed class TokenResponse
    {
        private string _Token = "";
        /// <summary>Opaque session token for Authorization: Bearer or x-token. It is not stored by the service.</summary>
        public string Token { get { return _Token; } set { ArgumentNullException.ThrowIfNull(value); _Token = value; } }

        private string _SessionId = "";
        /// <summary>Session ID, for listing and revoking sessions.</summary>
        public string SessionId { get { return _SessionId; } set { ArgumentNullException.ThrowIfNull(value); _SessionId = value; } }

        /// <summary>When the token stops working unless refreshed.</summary>
        public DateTime ExpiresUtc { get; set; }

        private User _User = new User();
        /// <summary>Signed-in user.</summary>
        public User User { get { return _User; } set { ArgumentNullException.ThrowIfNull(value); _User = value; } }

        private Tenant _Tenant = new Tenant();
        /// <summary>Tenant the session is bound to.</summary>
        public Tenant Tenant { get { return _Tenant; } set { ArgumentNullException.ThrowIfNull(value); _Tenant = value; } }
    }
}
