namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A security audit record: sign-ins, sessions, credentials, identity changes and every denied request.</summary>
    public sealed class AuditEvent
    {
        private string _Id = "";
        /// <summary>Audit record ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _TenantId = "";
        /// <summary>Tenant, when resolvable.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        private string _UserId = "";
        /// <summary>User, when resolvable.</summary>
        public string UserId { get { return _UserId; } set { ArgumentNullException.ThrowIfNull(value); _UserId = value; } }

        private string _Operation = "";
        /// <summary>Event type, for example AuthSuccess, AuthFailure, SessionIssued, SessionRevoked, CredentialUsed or AuthorizationDenied.</summary>
        public string Operation { get { return _Operation; } set { ArgumentNullException.ThrowIfNull(value); _Operation = value; } }

        private string _Outcome = "";
        /// <summary>success, failure or denied.</summary>
        public string Outcome { get { return _Outcome; } set { ArgumentNullException.ThrowIfNull(value); _Outcome = value; } }

        private string _PrincipalType = "";
        /// <summary>User or Credential.</summary>
        public string PrincipalType { get { return _PrincipalType; } set { ArgumentNullException.ThrowIfNull(value); _PrincipalType = value; } }

        private string _SessionId = "";
        /// <summary>Session, when used or created.</summary>
        public string SessionId { get { return _SessionId; } set { ArgumentNullException.ThrowIfNull(value); _SessionId = value; } }

        private string _CredentialId = "";
        /// <summary>Credential, when used.</summary>
        public string CredentialId { get { return _CredentialId; } set { ArgumentNullException.ThrowIfNull(value); _CredentialId = value; } }

        private string _AuthScheme = "";
        /// <summary>Authentication scheme.</summary>
        public string AuthScheme { get { return _AuthScheme; } set { ArgumentNullException.ThrowIfNull(value); _AuthScheme = value; } }

        private string _CorrelationId = "";
        /// <summary>Request ID.</summary>
        public string CorrelationId { get { return _CorrelationId; } set { ArgumentNullException.ThrowIfNull(value); _CorrelationId = value; } }

        private string _RequestType = "";
        /// <summary>Classified request type.</summary>
        public string RequestType { get { return _RequestType; } set { ArgumentNullException.ThrowIfNull(value); _RequestType = value; } }

        private string _Method = "";
        /// <summary>HTTP method.</summary>
        public string Method { get { return _Method; } set { ArgumentNullException.ThrowIfNull(value); _Method = value; } }

        private string _Path = "";
        /// <summary>URL path without the query.</summary>
        public string Path { get { return _Path; } set { ArgumentNullException.ThrowIfNull(value); _Path = value; } }

        private string _SourceIp = "";
        /// <summary>Client address.</summary>
        public string SourceIp { get { return _SourceIp; } set { ArgumentNullException.ThrowIfNull(value); _SourceIp = value; } }

        private string _AuthenticationResult = "";
        /// <summary>Success, NotFound, Inactive or Invalid.</summary>
        public string AuthenticationResult { get { return _AuthenticationResult; } set { ArgumentNullException.ThrowIfNull(value); _AuthenticationResult = value; } }

        private string _AuthorizationResult = "";
        /// <summary>Permitted, DeniedImplicit or NotFound.</summary>
        public string AuthorizationResult { get { return _AuthorizationResult; } set { ArgumentNullException.ThrowIfNull(value); _AuthorizationResult = value; } }

        private string _Reason = "";
        /// <summary>Free-text reason.</summary>
        public string Reason { get { return _Reason; } set { ArgumentNullException.ThrowIfNull(value); _Reason = value; } }

        private string _ResourceType = "";
        /// <summary>Affected resource type.</summary>
        public string ResourceType { get { return _ResourceType; } set { ArgumentNullException.ThrowIfNull(value); _ResourceType = value; } }

        private string _ResourceId = "";
        /// <summary>Affected resource ID.</summary>
        public string ResourceId { get { return _ResourceId; } set { ArgumentNullException.ThrowIfNull(value); _ResourceId = value; } }

        private string _ErrorCode = "";
        /// <summary>Error code returned.</summary>
        public string ErrorCode { get { return _ErrorCode; } set { ArgumentNullException.ThrowIfNull(value); _ErrorCode = value; } }

        /// <summary>HTTP status returned.</summary>
        public int StatusCode { get; set; }

        /// <summary>When the event happened.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
