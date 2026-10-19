namespace ArgueBot.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using ArgueBot.Core.Services;
    using ArgueBot.Runners.Processes;

    /// <summary>Selects the configured runner without moving execution into a UI.</summary>
    public sealed class RunnerRouter : IRunner
    {
        private readonly bool _AllowCli;
        /// <summary>Construct with the host's CLI execution policy.</summary>
        public RunnerRouter(bool allowCli) { _AllowCli = allowCli; }
        /// <inheritdoc />
        public Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        {
            if (profile.Kind == "cli" && !_AllowCli) throw new DomainException(403, "cli_disabled", "Shell-out execution is disabled on this server.");
            IRunner runner = profile.Kind == "cli" ? new CliRunner() : new PolyPromptRunner();
            return runner.GenerateAsync(profile, instructions, history, onText, token);
        }
    }
}
