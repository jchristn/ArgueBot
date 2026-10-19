namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;

    internal sealed class FinalPauseRunner : IRunner
    {
        private readonly TaskCompletionSource<bool> _Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _Calls;
        internal Task Started { get { return _Started.Task; } }
        internal int Calls { get { return Volatile.Read(ref _Calls); } }
        internal void Complete() { _Release.TrySetResult(true); }
        public async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        {
            Interlocked.Increment(ref _Calls); _Started.TrySetResult(true);
            await _Release.Task.WaitAsync(token).ConfigureAwait(false);
            await onText("Completed final answer.", token).ConfigureAwait(false);
        }
    }
}
