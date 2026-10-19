namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;

    internal sealed class SingleChunkRunner : IRunner
    {
        private readonly string _Text;
        internal SingleChunkRunner(string text) { _Text = text; }
        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return onText(_Text, token); }
    }
}
