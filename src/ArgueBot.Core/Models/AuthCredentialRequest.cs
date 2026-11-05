namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Create or update a credential.</summary>
    public sealed class AuthCredentialRequest
    {
        private string _Name = "";
        /// <summary>Display name, 1 through 200 characters.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>Optional expiry in UTC; must be in the future when creating.</summary>
        public DateTime? ExpiresUtc { get; set; }

        /// <summary>False rejects the token.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Expected revision when updating.</summary>
        public long Revision { get; set; }
    }
}
