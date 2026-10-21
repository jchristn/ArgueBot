namespace ArgueBot.Core.Models
{
    /// <summary>A service-owned local tool invocation checkpoint and its bounded result.</summary>
    public sealed class ToolActivity
    {
        /// <summary>Provider invocation identifier within the generation attempt.</summary>
        public string CallId { get; set; } = "";
        /// <summary>Requested tool name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Literal JSON arguments supplied by the model.</summary>
        public string ArgumentsJson { get; set; } = "{}";
        /// <summary>Started, completed, failed or interrupted.</summary>
        public string State { get; set; } = "started";
        /// <summary>Bounded result or diagnostic, empty before execution.</summary>
        public string Result { get; set; } = "";
    }
}
