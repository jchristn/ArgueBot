namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class ExecutionWorker
    {
        internal Task Completion { get; set; } = Task.CompletedTask;
        internal CancellationTokenSource? Generation { get; set; }
        internal bool PauseAfterTurn { get; set; }
        internal bool StopRequested { get; set; }
        internal bool SkipWindow { get; set; }
        internal ConcurrentDictionary<string, DateTime> Holds { get; } = new ConcurrentDictionary<string, DateTime>();
    }
}
