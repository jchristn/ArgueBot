namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;

    internal sealed class BurstRunner : IRunner
    {
        internal const string Fragment = "議論🙂";
        internal const int Fragments = 1000;
        public async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        {
            for (int index = 0; index < Fragments; index++)
                foreach (char character in Fragment) await onText(character.ToString(), token).ConfigureAwait(false);
        }
    }
}
