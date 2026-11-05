namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Options for a new public link.</summary>
    public sealed class ConversationShareRequest
    {
        /// <summary>Hours until the link stops working, 1 through 2160 (90 days).</summary>
        public int ExpiresInHours { get; set; } = 168;

        /// <summary>Show tool calls and results to readers.</summary>
        public bool IncludeToolCalls { get; set; }
    }
}
