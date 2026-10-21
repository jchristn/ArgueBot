namespace ArgueBot.Core.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Runner that exposes durable local tool activity independently of answer text.</summary>
    public interface IToolRunner : IRunner
    {
        /// <summary>Generate an answer, awaiting the tool checkpoint callback before each local operation.</summary>
        Task GenerateWithToolsAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, Func<ToolActivity, CancellationToken, Task> onTool, CancellationToken token = default);
    }
}
