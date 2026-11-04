namespace ArgueBot.Core.Models
{
    /// <summary>Typed conversation-tool request; run and recipient scope come from the invocation.</summary>
    public sealed class ConversationToolCall
    {
        /// <summary>Stable call identifier within the invocation.</summary>
        public string CallId { get; set; } = "";
        /// <summary>Advertised tool name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Bounded JSON arguments validated against the tool contract.</summary>
        public string ArgumentsJson { get; set; } = "{}";
    }
}
