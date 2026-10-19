namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Package provenance captured from committed dependency locks and published package metadata.</summary>
    public sealed class DependencyReference
    {
        private string _Name = "";
        private string _Version = "";
        private string _Ecosystem = "";
        private string _License = "";
        private string _Source = "";
        private string _Integrity = "";

        /// <summary>Package name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }
        /// <summary>Exact package version.</summary>
        public string Version { get { return _Version; } set { ArgumentNullException.ThrowIfNull(value); _Version = value; } }
        /// <summary>Package ecosystem, nuget or npm.</summary>
        public string Ecosystem { get { return _Ecosystem; } set { ArgumentNullException.ThrowIfNull(value); _Ecosystem = value; } }
        /// <summary>Published license expression or license-file declaration.</summary>
        public string License { get { return _License; } set { ArgumentNullException.ThrowIfNull(value); _License = value; } }
        /// <summary>Published package reference.</summary>
        public string Source { get { return _Source; } set { ArgumentNullException.ThrowIfNull(value); _Source = value; } }
        /// <summary>Locked content hash.</summary>
        public string Integrity { get { return _Integrity; } set { ArgumentNullException.ThrowIfNull(value); _Integrity = value; } }
        /// <summary>Whether the package is confined to development/test tooling.</summary>
        public bool Development { get; set; }
    }
}
