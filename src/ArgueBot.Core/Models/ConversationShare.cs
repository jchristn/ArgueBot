namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A time-bound public link to a conversation transcript. The link token is never stored or returned after creation.</summary>
    public sealed class ConversationShare
    {
        private string _Id = "";
        /// <summary>Share ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _ConversationId = "";
        /// <summary>Shared conversation.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        private string _CreatedByUserId = "";
        /// <summary>User who created the link.</summary>
        public string CreatedByUserId { get { return _CreatedByUserId; } set { ArgumentNullException.ThrowIfNull(value); _CreatedByUserId = value; } }

        private string _TokenLast4 = "";
        /// <summary>Last four characters of the link token, to tell links apart.</summary>
        public string TokenLast4 { get { return _TokenLast4; } set { ArgumentNullException.ThrowIfNull(value); _TokenLast4 = value; } }

        /// <summary>Whether readers see tool calls and results.</summary>
        public bool IncludeToolCalls { get; set; }

        /// <summary>When the link stops working.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>When the link was revoked.</summary>
        public DateTime? RevokedUtc { get; set; }

        /// <summary>Last time someone opened the link.</summary>
        public DateTime? LastViewedUtc { get; set; }

        /// <summary>How many times the link was opened.</summary>
        public long ViewCount { get; set; }

        /// <summary>False once revoked or expired.</summary>
        public bool Active { get; set; }

        /// <summary>When the link was created.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
