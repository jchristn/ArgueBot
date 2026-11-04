namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>CredentialRequest configuration contract.</summary>
    public sealed class CredentialRequest
    {
        private string _Name = "";
        /// <summary>Safe display name.</summary>
        public string Name { get { return _Name; } set { System.ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Kind = "token";
        /// <summary>token, claude_oauth or codex_auth_json.</summary>
        public string Kind { get { return _Kind; } set { System.ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        private string _SecretValue = "";
        /// <summary>Write-only secret; never returned or recorded in request history.</summary>
        public string SecretValue { get { return _SecretValue; } set { System.ArgumentNullException.ThrowIfNull(value); _SecretValue = value; } }

        /// <summary>Expected revision for replacement.</summary>
        public long Revision { get; set; } = 0;

    }
}
