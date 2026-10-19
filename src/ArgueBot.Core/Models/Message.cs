namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Message transport and domain data.</summary>
    public sealed class Message
    {
        private string _Id = "";
        private string _ConversationId = "";
        private string _Speaker = "user";
        private string _Kind = "note";
        private string _Content = "";
        private string _State = "completed";

        /// <summary>Stable message identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Containing conversation.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        /// <summary>Optional run.</summary>
        public string? RunId { get; set; }

        /// <summary>Conversation message order.</summary>
        public long Sequence { get; set; }

        /// <summary>Speaker snapshot.</summary>
        public string Speaker { get { return _Speaker; } set { ArgumentNullException.ThrowIfNull(value); _Speaker = value; } }

        /// <summary>Semantic message kind.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        /// <summary>Original message text.</summary>
        public string Content { get { return _Content; } set { ArgumentNullException.ThrowIfNull(value); _Content = value; } }

        /// <summary>Private directive recipient, if present.</summary>
        public string? RecipientId { get; set; }

        /// <summary>Generation lifecycle state.</summary>
        public string State { get { return _State; } set { ArgumentNullException.ThrowIfNull(value); _State = value; } }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

    }
}
