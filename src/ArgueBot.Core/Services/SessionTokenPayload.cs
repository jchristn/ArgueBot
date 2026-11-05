namespace ArgueBot.Core.Services
{
    using System;

    /// <summary>The encrypted contents of a session token. Only platform-controlled identifiers are carried.</summary>
    internal sealed class SessionTokenPayload
    {
        /// <summary>Session ID.</summary>
        public string Sid { get; set; } = "";
        /// <summary>Principal type: User or Credential.</summary>
        public string Typ { get; set; } = "";
        /// <summary>User ID.</summary>
        public string Usr { get; set; } = "";
        /// <summary>Credential ID, when the session came from a credential.</summary>
        public string Crd { get; set; } = "";
        /// <summary>Tenant ID.</summary>
        public string Ten { get; set; } = "";
        /// <summary>Issued time.</summary>
        public DateTime Iat { get; set; }
        /// <summary>Expiry time.</summary>
        public DateTime Exp { get; set; }
        /// <summary>Random token identifier, unique per issued token.</summary>
        public string Jti { get; set; } = "";
        /// <summary>Authentication scheme that created the session.</summary>
        public string Sch { get; set; } = "";
    }
}
