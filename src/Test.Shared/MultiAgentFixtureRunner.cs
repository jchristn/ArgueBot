namespace Test.Shared
{
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Runners;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class MultiAgentFixtureRunner : IRunner
    {
        internal string HoldPhase { get; set; } = "";
        internal bool Disagree { get; set; }
        internal Dictionary<string, string> Positions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        internal Dictionary<string, string> RawRatification { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        internal System.Collections.Concurrent.ConcurrentDictionary<string, string> RawOnce { get; } = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Failure codes thrown once for a step key (same keys as RawOnce), as a working agent that fails intermittently would.</summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<string, string> FailOnce { get; } = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Step keys whose next vote on a later proposal names the first proposal instead, as a stale vote would.</summary>
        internal HashSet<string> StaleOnce { get; } = new HashSet<string>(StringComparer.Ordinal);
        private string _FirstProposal = "";
        internal System.Collections.Concurrent.ConcurrentQueue<string> Prompts { get; } = new System.Collections.Concurrent.ConcurrentQueue<string>();
        internal bool HoldResponse { get; set; }
        internal TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _Held;
        private int _PhaseCalls;
        internal int HoldAfter { get; set; }
        public async Task GenerateAsync(RunnerProfile profile, string instructions, IReadOnlyList<Message> history, Func<string, CancellationToken, Task> onText, CancellationToken token = default)
        {
            Prompts.Enqueue(profile.Name + "\n" + instructions + "\n" + string.Join("\n", history.Select(message => message.Content)));
            string phase = instructions.Contains("question (required", StringComparison.Ordinal) ? "question" : instructions.Contains("proposal (required", StringComparison.Ordinal) ? "synthesis" : instructions.Contains("proposalId:", StringComparison.Ordinal) ? "ratification" : "response";
            if (HoldPhase == phase && Interlocked.Increment(ref _PhaseCalls) > HoldAfter && Interlocked.Exchange(ref _Held, 1) == 0)
            { Started.TrySetResult(true); await Release.Task.WaitAsync(token).ConfigureAwait(false); }
            string output;
            if (instructions.Contains("question (required", StringComparison.Ordinal)) output = JsonSerializer.Serialize(new { question = "Which evidence distinguishes the alternatives?", rationale = "Ask every participant to evaluate evidence." });
            else if (instructions.Contains("proposal (required", StringComparison.Ordinal)) output = JsonSerializer.Serialize(new { proposal = "Choose the reversible design, with a measured rollout." });
            else if (instructions.Contains("proposalId:", StringComparison.Ordinal))
            {
                string proposal = Regex.Match(instructions, "proposalId: \"([^\"]+)\"").Groups[1].Value;
                lock (StaleOnce)
                {
                    if (_FirstProposal.Length == 0) _FirstProposal = proposal;
                    else if (proposal != _FirstProposal && StaleOnce.Remove(profile.Name + ":ratification")) proposal = _FirstProposal;
                }
                string position = Positions.TryGetValue(profile.Name, out string? configured) ? configured : Disagree && profile.Name == "Agent 1" ? "disagree" : "agree";
                output = RawRatification.TryGetValue(profile.Name, out string? raw) ? raw : JsonSerializer.Serialize(new { proposalId = proposal, position, rationale = "Fictional test position." });
            }
            else
            {
                if (HoldResponse && Interlocked.Exchange(ref _Held, 1) == 0)
                { Started.TrySetResult(true); await Release.Task.WaitAsync(token).ConfigureAwait(false); }
                output = "WE HAVE CONSENSUS\nThis phrase alone must not finish a protocol 2 debate.";
            }
            string key = phase == "ratification" || phase == "response" ? profile.Name + ":" + phase : phase;
            if (FailOnce.TryRemove(key, out string? code)) throw new ArgueBot.Core.Services.DomainException(503, code, "Fictional intermittent failure.");
            if (RawOnce.TryRemove(key, out string? once)) output = once;
            await onText(output, token).ConfigureAwait(false);
        }
    }
}
