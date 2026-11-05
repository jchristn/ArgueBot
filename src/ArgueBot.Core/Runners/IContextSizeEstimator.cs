namespace ArgueBot.Core.Runners
{
    using ArgueBot.Core.Models;
    using System.Collections.Generic;

    /// <summary>Transport-aware initial request sizing before an invocation is durably accepted.</summary>
    public interface IContextSizeEstimator
    {
        /// <summary>Measure initial request characters, including serialized tool definitions and message framing.</summary>
        long MeasureContext(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, bool allowLocalTools);
    }
}
