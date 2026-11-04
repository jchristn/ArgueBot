namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Immutable original-source identity and content hash captured when a summary was accepted.</summary>
    public sealed class SummarySource
    {
        private string _MessageId = "";
        /// <summary>Original conversation message identifier.</summary>
        public string MessageId { get { return _MessageId; } set { ArgumentNullException.ThrowIfNull(value); _MessageId = value; } }
        private string _Digest = "";
        /// <summary>Lowercase SHA-256 of the exact original UTF-8 content.</summary>
        public string Digest { get { return _Digest; } set { ArgumentNullException.ThrowIfNull(value); _Digest = value; } }
    }
}
