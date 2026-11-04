namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>HttpAuthBinding configuration contract.</summary>
    public sealed class HttpAuthBinding
    {
        private string _Kind = "header";
        /// <summary>Authentication kind: bearer, api_key, header or query.</summary>
        public string Kind { get { return _Kind; } set { System.ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        private string _Name = "";
        /// <summary>Header or query parameter name; bearer uses Authorization.</summary>
        public string Name { get { return _Name; } set { System.ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Value = "";
        /// <summary>Nonsecret static routing value; secret values use a credential reference.</summary>
        public string Value { get { return _Value; } set { System.ArgumentNullException.ThrowIfNull(value); _Value = value; } }

        private string _CredentialId = "";
        /// <summary>Write-only credential reference.</summary>
        public string CredentialId { get { return _CredentialId; } set { System.ArgumentNullException.ThrowIfNull(value); _CredentialId = value; } }

        private string _EnvironmentVariable = "";
        /// <summary>Service environment reference, mutually exclusive with value or credential.</summary>
        public string EnvironmentVariable { get { return _EnvironmentVariable; } set { System.ArgumentNullException.ThrowIfNull(value); _EnvironmentVariable = value; } }

    }
}
