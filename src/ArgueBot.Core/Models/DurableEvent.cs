namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>DurableEvent transport and domain data.</summary>
    public sealed class DurableEvent
    {
        private string _ConversationId = "";
        private string _Kind = "";
        private string _EntityId = "";
        private string _Text = "";

        /// <summary>Durable conversation event order.</summary>
        public long Sequence { get; set; }

        /// <summary>Containing conversation.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        /// <summary>Semantic event type.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        /// <summary>Changed entity.</summary>
        public string EntityId { get { return _EntityId; } set { ArgumentNullException.ThrowIfNull(value); _EntityId = value; } }

        /// <summary>Optional text delta or safe detail.</summary>
        public string Text { get { return _Text; } set { ArgumentNullException.ThrowIfNull(value); _Text = value; } }

        /// <summary>UTF-16 code-unit offset for this committed text fragment within its message. Legacy events before schema 6 use zero and require snapshot reconciliation.</summary>
        public long TextOffset { get; set; }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

    }
}
