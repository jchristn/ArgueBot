namespace ArgueBot.Runners
{
    using System;
    using System.Threading;
    using ArgueBot.Core.Models;

    /// <summary>Starts a runner's turn deadline on the service's pausable invocation clock when one is supplied.</summary>
    internal static class RunnerDeadline
    {
        /// <summary>Start the profile's timeout on the source. With a service clock the deadline pauses while the agent waits for the user.</summary>
        internal static IDisposable Start(RunnerProfile profile, CancellationTokenSource source)
        {
            TimeSpan budget = TimeSpan.FromSeconds(profile.TimeoutSeconds);
            if (profile.RuntimeClock != null) return profile.RuntimeClock.Attach(source, budget);
            source.CancelAfter(budget);
            return new Empty();
        }

        private sealed class Empty : IDisposable { public void Dispose() { } }
    }
}
