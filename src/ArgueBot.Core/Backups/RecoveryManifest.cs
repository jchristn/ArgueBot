namespace ArgueBot.Core.Backups
{
    using System.Collections.Generic;
    using System;
    using ArgueBot.Core.Models;

    /// <summary>Recovery bundle inventory and provenance, independent of its outer optional encryption envelope.</summary>
    public sealed class RecoveryManifest
    {
        private string _Provider = "";
        private string _ApplicationVersion = "";
        private string _ConfigurationRevision = "";
        private List<string> _SecretReferences = new List<string>();
        private List<RecoveryFile> _Files = new List<RecoveryFile>();
        private List<DependencyReference> _Dependencies = new List<DependencyReference>();

        /// <summary>Recovery interchange version.</summary>
        public int FormatVersion { get; set; } = 1;
        /// <summary>Database schema compatibility.</summary>
        public int SchemaVersion { get; set; }
        /// <summary>Source database provider.</summary>
        public string Provider { get { return _Provider; } set { ArgumentNullException.ThrowIfNull(value); _Provider = value; } }
        /// <summary>Application informational version, including available build provenance.</summary>
        public string ApplicationVersion { get { return _ApplicationVersion; } set { ArgumentNullException.ThrowIfNull(value); _ApplicationVersion = value; } }
        /// <summary>UTC bundle creation time.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>Whether explicitly requested session keys and raw connection configuration are included.</summary>
        public bool IncludesSecrets { get; set; }
        /// <summary>Archived settings revision, with raw connection credentials excluded by default.</summary>
        public string ConfigurationRevision { get { return _ConfigurationRevision; } set { ArgumentNullException.ThrowIfNull(value); _ConfigurationRevision = value; } }
        /// <summary>Environment credential references that operators may need to reconfigure.</summary>
        public List<string> SecretReferences { get { return _SecretReferences; } set { ArgumentNullException.ThrowIfNull(value); _SecretReferences = value; } }
        /// <summary>Exact allowlisted content inventory.</summary>
        public List<RecoveryFile> Files { get { return _Files; } set { ArgumentNullException.ThrowIfNull(value); _Files = value; } }
        /// <summary>Locked dependency references captured by the build.</summary>
        public List<DependencyReference> Dependencies { get { return _Dependencies; } set { ArgumentNullException.ThrowIfNull(value); _Dependencies = value; } }
    }
}
