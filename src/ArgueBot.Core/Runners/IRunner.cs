namespace ArgueBot.Core.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Replaceable streaming execution boundary. No persistence or UI ownership.</summary>
    public interface IRunner
    {
        /// <summary>Generate a response, reporting answer deltas in order. Cancellation must stop external work.</summary>
        Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default);
    }
}
