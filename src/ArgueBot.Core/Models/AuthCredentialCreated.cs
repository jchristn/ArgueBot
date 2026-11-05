namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A newly created credential together with its bearer token. Later it can be revealed again by its owner or a tenant administrator, with an audit record.</summary>
    public sealed class AuthCredentialCreated
    {
        private AuthCredential _Credential = new AuthCredential();
        /// <summary>The stored credential.</summary>
        public AuthCredential Credential { get { return _Credential; } set { ArgumentNullException.ThrowIfNull(value); _Credential = value; } }

        private string _Token = "";
        /// <summary>The bearer token. Store it now; it cannot be retrieved later.</summary>
        public string Token { get { return _Token; } set { ArgumentNullException.ThrowIfNull(value); _Token = value; } }
    }
}
