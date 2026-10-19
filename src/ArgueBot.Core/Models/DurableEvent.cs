namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>DurableEvent transport and domain data.</summary>
    public sealed class DurableEvent
    {
        /// <summary>Durable conversation event order.</summary>
        public long Sequence { get; set; }

        /// <summary>Containing conversation.</summary>
        public string ConversationId { get; set; } = "";

        /// <summary>Semantic event type.</summary>
        public string Kind { get; set; } = "";

        /// <summary>Changed entity.</summary>
        public string EntityId { get; set; } = "";

        /// <summary>Optional text delta or safe detail.</summary>
        public string Text { get; set; } = "";

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

    }
}
