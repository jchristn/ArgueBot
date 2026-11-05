namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>CredentialMetadata configuration contract.</summary>
    public sealed class CredentialMetadata
    {
        private string _Id = "";
        /// <summary>Stable write-only credential reference.</summary>
        public string Id { get { return _Id; } set { System.ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Safe display name.</summary>
        public string Name { get { return _Name; } set { System.ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Kind = "token";
        /// <summary>token, claude_oauth or codex_auth_json.</summary>
        public string Kind { get { return _Kind; } set { System.ArgumentNullException.ThrowIfNull(value); _Kind = value; } }


        private string _ParameterName = "";
        /// <summary>Header or query parameter name for http_header and http_query credentials; empty for every other kind. Fixed after creation.</summary>
        public string ParameterName { get { return _ParameterName; } set { System.ArgumentNullException.ThrowIfNull(value); _ParameterName = value; } }
        private string _Status = "ready";
        /// <summary>ready; disabled; or refresh_uncertain when a Codex token refresh was sent but its result is unknown, so the stored refresh token must not be replayed and the login must be replaced.</summary>
        public string Status { get { return _Status; } set { System.ArgumentNullException.ThrowIfNull(value); _Status = value; } }

        /// <summary>Current credential revision; replacement requires this value.</summary>
        public long Revision { get; set; } = 0;

    }
}
