namespace ArgueBot.Core.Runners
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Runner boundary for invocation-scoped conversation tools, independent of local execution permission.</summary>
    public interface IConversationToolRunner : IToolRunner
    {
        /// <summary>Generate with a bounded tool catalogue and server-controlled recipient scope.</summary>
        Task GenerateWithContextAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, IReadOnlyList<ConversationToolDefinition> tools, Func<ConversationToolCall, CancellationToken, Task<string>> execute, bool allowLocalTools, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default);
    }
}
