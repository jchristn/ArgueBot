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
        internal bool InterruptRequested { get; set; }
        internal bool StopRequested { get; set; }
        internal bool SkipWindow { get; set; }
        /// <summary>Whether this worker holds a service run-capacity slot. A turn waiting for the user releases it.</summary>
        internal bool CapacityHeld { get; set; }
        internal ConcurrentDictionary<string, long> Holds { get; } = new ConcurrentDictionary<string, long>();
    }
}
