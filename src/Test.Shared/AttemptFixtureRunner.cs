namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;

    internal sealed class AttemptFixtureRunner : IRunner
    {
        private readonly TaskCompletionSource<bool> _FirstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _Calls;
        internal Task FirstStarted { get { return _FirstStarted.Task; } }
        internal ConcurrentQueue<string> Prompts { get; } = new ConcurrentQueue<string>();
        internal ConcurrentQueue<string[]> Contexts { get; } = new ConcurrentQueue<string[]>();
        public async Task GenerateAsync(RunnerProfile profile, string systemPrompt, IReadOnlyList<Message> messages, Func<string, CancellationToken, Task> onDelta, CancellationToken token = default)
        {
            Prompts.Enqueue(systemPrompt); Contexts.Enqueue(messages.Select(message => message.Id).ToArray());
            int call = Interlocked.Increment(ref _Calls);
            if (call == 1)
            {
                await onDelta("uncertain partial answer", token).ConfigureAwait(false); _FirstStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }
            else await onDelta("Completed fixture answer " + call, token).ConfigureAwait(false);
        }
    }
}
