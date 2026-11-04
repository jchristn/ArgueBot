namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>A credential's decrypted value, returned only by the explicit reveal operation for the moderator.</summary>
    public sealed class RevealedCredential
    {
        private string _Id = "";
        /// <summary>Credential identifier.</summary>
        public string Id { get { return _Id; } set { System.ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Display name.</summary>
        public string Name { get { return _Name; } set { System.ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Kind = "";
        /// <summary>Credential kind.</summary>
        public string Kind { get { return _Kind; } set { System.ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        private string _ParameterName = "";
        /// <summary>Header or query parameter name for header and query credentials; empty otherwise.</summary>
        public string ParameterName { get { return _ParameterName; } set { System.ArgumentNullException.ThrowIfNull(value); _ParameterName = value; } }

        /// <summary>Revision of the revealed value.</summary>
        public long Revision { get; set; } = 0;

        private string _SecretValue = "";
        /// <summary>The decrypted value. A Codex login includes the refresh token the service manages.</summary>
        public string SecretValue { get { return _SecretValue; } set { System.ArgumentNullException.ThrowIfNull(value); _SecretValue = value; } }
    }
}
