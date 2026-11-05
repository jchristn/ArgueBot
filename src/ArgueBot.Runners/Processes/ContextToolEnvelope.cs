namespace ArgueBot.Runners.Processes
{
    using ArgueBot.Core.Models;

    internal sealed class ContextToolEnvelope
    {
        public ConversationToolCall? ContextTool { get; set; }
    }
}
