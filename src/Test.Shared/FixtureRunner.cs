namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;

    internal sealed class FixtureRunner : IRunner
    {
        private int _Calls;
        internal int Calls { get { return _Calls; } }
        internal bool Wait { get; set; }
        internal TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <inheritdoc />
        public async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        {
            Interlocked.Increment(ref _Calls); Started.TrySetResult(true);
            await onText("A concrete argument. ", token).ConfigureAwait(false);
            if (Wait) await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            await onText("Complete.", token).ConfigureAwait(false);
        }
    }
}
