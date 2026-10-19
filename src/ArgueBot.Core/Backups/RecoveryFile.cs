namespace ArgueBot.Core.Backups
{
    using System;

    /// <summary>One allowlisted recovery entry, with exact size and SHA-256.</summary>
    public sealed class RecoveryFile
    {
        private string _Path = "";
        private string _Digest = "";

        /// <summary>Relative archive path.</summary>
        public string Path { get { return _Path; } set { ArgumentNullException.ThrowIfNull(value); _Path = value; } }
        /// <summary>Uncompressed byte count.</summary>
        public long Bytes { get; set; }
        /// <summary>SHA-256 digest in hexadecimal.</summary>
        public string Digest { get { return _Digest; } set { ArgumentNullException.ThrowIfNull(value); _Digest = value; } }
    }
}
