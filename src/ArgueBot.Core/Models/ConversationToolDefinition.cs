namespace ArgueBot.Core.Models
{
    /// <summary>Shared REST/CLI tool schema exposed only within a service-owned invocation.</summary>
    public sealed class ConversationToolDefinition
    {
        /// <summary>Tool name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Purpose and limits.</summary>
        public string Description { get; set; } = "";
        /// <summary>JSON schema for typed arguments.</summary>
        public string Schema { get; set; } = "{}";
    }
}
