namespace ArgueBot.Client
{
    using System;

    /// <summary>A signed-in session remembered for an application home: the server and its session token, never the password.</summary>
    public sealed class ClientSession
    {
        private string _Server = "";
        /// <summary>Service URL the token belongs to.</summary>
        public string Server { get { return _Server; } set { ArgumentNullException.ThrowIfNull(value); _Server = value; } }

        private string _Token = "";
        /// <summary>Session token (abs_...) or credential token.</summary>
        public string Token { get { return _Token; } set { ArgumentNullException.ThrowIfNull(value); _Token = value; } }

        /// <summary>When a session token expires unless refreshed; null for a credential token.</summary>
        public DateTime? ExpiresUtc { get; set; }

        private string _Email = "";
        /// <summary>Signed-in email, to prefill the next sign-in.</summary>
        public string Email { get { return _Email; } set { ArgumentNullException.ThrowIfNull(value); _Email = value; } }

        private string _TenantName = "";
        /// <summary>Tenant name, for display.</summary>
        public string TenantName { get { return _TenantName; } set { ArgumentNullException.ThrowIfNull(value); _TenantName = value; } }

        /// <summary>True when the user allowed plain HTTP to this remote server.</summary>
        public bool AllowInsecureHttp { get; set; }
    }
}
