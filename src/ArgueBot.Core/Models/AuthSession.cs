namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A revocable sign-in session. The session token itself is never stored or returned after issue.</summary>
    public sealed class AuthSession
    {
        private string _Id = "";
        /// <summary>Session ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _TenantId = "";
        /// <summary>Tenant the session is bound to.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        private string _UserId = "";
        /// <summary>Signed-in user.</summary>
        public string UserId { get { return _UserId; } set { ArgumentNullException.ThrowIfNull(value); _UserId = value; } }

        private string _CredentialId = "";
        /// <summary>Credential that created the session, or empty for a password sign-in.</summary>
        public string CredentialId { get { return _CredentialId; } set { ArgumentNullException.ThrowIfNull(value); _CredentialId = value; } }

        private string _PrincipalType = "";
        /// <summary>User or Credential.</summary>
        public string PrincipalType { get { return _PrincipalType; } set { ArgumentNullException.ThrowIfNull(value); _PrincipalType = value; } }

        private string _AuthScheme = "";
        /// <summary>How the session was created, for example PasswordHeaders or PasswordBody.</summary>
        public string AuthScheme { get { return _AuthScheme; } set { ArgumentNullException.ThrowIfNull(value); _AuthScheme = value; } }

        private string _SourceIp = "";
        /// <summary>Address that created the session.</summary>
        public string SourceIp { get { return _SourceIp; } set { ArgumentNullException.ThrowIfNull(value); _SourceIp = value; } }

        private string _UserAgent = "";
        /// <summary>Client that created the session.</summary>
        public string UserAgent { get { return _UserAgent; } set { ArgumentNullException.ThrowIfNull(value); _UserAgent = value; } }

        /// <summary>When the session was issued.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>When the session stops working unless refreshed.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>Last authenticated request, updated at most once a minute.</summary>
        public DateTime? LastUsedUtc { get; set; }

        /// <summary>When the session was revoked.</summary>
        public DateTime? RevokedUtc { get; set; }

        private string _RevocationReason = "";
        /// <summary>Why the session was revoked.</summary>
        public string RevocationReason { get { return _RevocationReason; } set { ArgumentNullException.ThrowIfNull(value); _RevocationReason = value; } }

        /// <summary>False once revoked.</summary>
        public bool Active { get; set; }

        /// <summary>True for the session that made this request.</summary>
        public bool Current { get; set; }
    }
}
