namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A bearer-token credential's token, revealed to its owner or a tenant administrator. Handle it like a password.</summary>
    public sealed class RevealedToken
    {
        private string _Id = "";
        /// <summary>Credential ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Credential name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Token = "";
        /// <summary>The bearer token.</summary>
        public string Token { get { return _Token; } set { ArgumentNullException.ThrowIfNull(value); _Token = value; } }
    }
}
