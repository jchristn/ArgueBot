namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A bearer-token credential owned by one user. The token itself appears only when created or explicitly revealed.</summary>
    public sealed class AuthCredential
    {
        private string _Id = "";
        /// <summary>Credential identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _TenantId = "";
        /// <summary>Owning tenant.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        private string _UserId = "";
        /// <summary>Owning user.</summary>
        public string UserId { get { return _UserId; } set { ArgumentNullException.ThrowIfNull(value); _UserId = value; } }

        private string _Name = "";
        /// <summary>Display name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _TokenLast4 = "";
        /// <summary>Last four characters of the token, for recognition.</summary>
        public string TokenLast4 { get { return _TokenLast4; } set { ArgumentNullException.ThrowIfNull(value); _TokenLast4 = value; } }

        private string _AuthMode = "DirectHeader";
        /// <summary>Authentication mode; always DirectHeader (bearer token).</summary>
        public string AuthMode { get { return _AuthMode; } set { ArgumentNullException.ThrowIfNull(value); _AuthMode = value; } }

        /// <summary>False rejects the token.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Protected credentials cannot be deleted or disabled.</summary>
        public bool Protected { get; set; }

        /// <summary>Optional expiry in UTC.</summary>
        public DateTime? ExpiresUtc { get; set; }

        /// <summary>Last time the token authenticated a request.</summary>
        public DateTime? LastUsedUtc { get; set; }

        /// <summary>Optimistic revision.</summary>
        public long Revision { get; set; }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Last change time in UTC.</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
