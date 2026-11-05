namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>403 response explaining a denied request without listing the caller's permissions.</summary>
    public sealed class AuthorizationError
    {
        private string _Code = "";
        /// <summary>Machine-readable error code, for example forbidden or tenant_mismatch.</summary>
        public string Code { get { return _Code; } set { ArgumentNullException.ThrowIfNull(value); _Code = value; } }

        private string _Message = "";
        /// <summary>Human-readable explanation.</summary>
        public string Message { get { return _Message; } set { ArgumentNullException.ThrowIfNull(value); _Message = value; } }

        private string _Reason = "";
        /// <summary>Short reason for the denial.</summary>
        public string Reason { get { return _Reason; } set { ArgumentNullException.ThrowIfNull(value); _Reason = value; } }

        private string _RequiredPermission = "";
        /// <summary>Access class the route needs: Member, TenantAdmin or SystemAdmin.</summary>
        public string RequiredPermission { get { return _RequiredPermission; } set { ArgumentNullException.ThrowIfNull(value); _RequiredPermission = value; } }

        private string _RequestType = "";
        /// <summary>Classified request, for example GET /v2.0/api/users.</summary>
        public string RequestType { get { return _RequestType; } set { ArgumentNullException.ThrowIfNull(value); _RequestType = value; } }

        private string _TenantId = "";
        /// <summary>Tenant of the caller.</summary>
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        private string _RequestId = "";
        /// <summary>Correlation ID for request history and audit.</summary>
        public string RequestId { get { return _RequestId; } set { ArgumentNullException.ThrowIfNull(value); _RequestId = value; } }
    }
}
