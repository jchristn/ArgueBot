namespace ArgueBot.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Observability;
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
        public async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        {
            if (profile.Kind == "cli" && !_AllowCli) throw new DomainException(403, "cli_disabled", "Shell-out execution is disabled on this server.");
            IRunner runner = profile.Kind == "cli" ? new CliRunner() : new PolyPromptRunner();
            long started = Stopwatch.GetTimestamp(); string outcome = "failed"; bool first = true;
            using (Activity? activity = ProductTelemetry.Activities.StartActivity("runner.generate"))
            {
                activity?.SetTag("runner.kind", profile.Kind); activity?.SetTag("runner.provider", profile.Provider);
                try
                {
                    await runner.GenerateAsync(profile, instructions, history, async (delta, cancellation) =>
                    {
                        if (first && delta.Length > 0) { first = false; ProductTelemetry.FirstTextDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("runner", profile.Kind)); }
                        await onText(delta, cancellation).ConfigureAwait(false);
                    }, token).ConfigureAwait(false);
                    outcome = "success";
                }
                catch (OperationCanceledException) { outcome = "cancelled"; activity?.SetStatus(ActivityStatusCode.Error); throw; }
                catch { activity?.SetStatus(ActivityStatusCode.Error); throw; }
                finally { TagList tags = new TagList { { "runner", profile.Kind }, { "outcome", outcome } }; ProductTelemetry.Generations.Add(1, tags); ProductTelemetry.GenerationDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags); }
            }
        }
    }
}
