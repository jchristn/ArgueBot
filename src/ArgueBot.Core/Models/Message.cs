namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Message transport and domain data.</summary>
    public sealed class Message
    {
        /// <summary>Stable message identifier.</summary>
        public string Id { get; set; } = "";

        /// <summary>Containing conversation.</summary>
        public string ConversationId { get; set; } = "";

        /// <summary>Optional run.</summary>
        public string? RunId { get; set; }

        /// <summary>Conversation message order.</summary>
        public long Sequence { get; set; }

        /// <summary>Speaker snapshot.</summary>
        public string Speaker { get; set; } = "user";

        /// <summary>Semantic message kind.</summary>
        public string Kind { get; set; } = "note";

        /// <summary>Original message text.</summary>
        public string Content { get; set; } = "";

        /// <summary>Private directive recipient, if present.</summary>
        public string? RecipientId { get; set; }

        /// <summary>Generation lifecycle state.</summary>
        public string State { get; set; } = "completed";

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

    }
}
