namespace ArgueBot.Core.Backups
{
    using System.Collections.Generic;
    using System;

    /// <summary>Controlled recovery result, without secret values or automatic execution.</summary>
    public sealed class RecoveryRestoreResult
    {
        private string _Home = "";
        private string _Provider = "Sqlite";
        private List<string> _MissingReferences = new List<string>();

        /// <summary>Published destination application home.</summary>
        public string Home { get { return _Home; } set { ArgumentNullException.ThrowIfNull(value); _Home = value; } }
        /// <summary>Destination storage provider.</summary>
        public string Provider { get { return _Provider; } set { ArgumentNullException.ThrowIfNull(value); _Provider = value; } }
        /// <summary>Restored sessions were invalidated before the home became available.</summary>
        public bool SessionsRevoked { get; set; }
        /// <summary>Whether historical key material was explicitly included.</summary>
        public bool KeysRestored { get; set; }
        /// <summary>Referenced credential names missing from the restore process environment.</summary>
        public List<string> MissingReferences { get { return _MissingReferences; } set { ArgumentNullException.ThrowIfNull(value); _MissingReferences = value; } }
    }
}
