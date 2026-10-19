namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Global administrator-only recovery artifact metadata; archive contents remain protected separately.</summary>
    public sealed class BackupEntry
    {
        private string _Id = "";
        private string _Name = "";
        private string _Provider = "";
        private string _Digest = "";
        private string _Status = "";

        /// <summary>Opaque artifact identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        /// <summary>Operator's display name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }
        /// <summary>Published archive size.</summary>
        public long Bytes { get; set; }
        /// <summary>Whether the archive is passphrase encrypted.</summary>
        public bool Encrypted { get; set; }
        /// <summary>Whether explicitly requested key material is included.</summary>
        public bool IncludesSecrets { get; set; }
        /// <summary>Captured schema version.</summary>
        public int SchemaVersion { get; set; }
        /// <summary>Source storage provider.</summary>
        public string Provider { get { return _Provider; } set { ArgumentNullException.ThrowIfNull(value); _Provider = value; } }
        /// <summary>Published archive SHA-256.</summary>
        public string Digest { get { return _Digest; } set { ArgumentNullException.ThrowIfNull(value); _Digest = value; } }
        /// <summary>Creating, ready, failed or unavailable.</summary>
        public string Status { get { return _Status; } set { ArgumentNullException.ThrowIfNull(value); _Status = value; } }
        /// <summary>UTC creation time.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
