namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>Pausable invocation deadlines. Runners attach their timeout sources; while the agent waits for the user's answer the service pauses them, so waiting never counts against the agent's turn timeout.</summary>
    public sealed class InvocationClock
    {
        private readonly object _Lock = new object();
        private readonly TimeProvider _Time;
        private readonly List<Entry> _Entries = new List<Entry>();
        private int _Pauses;

        /// <summary>Create a clock.</summary>
        /// <param name="time">Time source; the system clock when null.</param>
        public InvocationClock(TimeProvider? time = null) { _Time = time ?? TimeProvider.System; }

        /// <summary>Whether the clock is paused.</summary>
        public bool Paused { get { lock (_Lock) { return _Pauses > 0; } } }

        /// <summary>Start a deadline on the source and attach it, honoring a pause already in effect.</summary>
        /// <param name="source">Timeout source owned by the runner.</param>
        /// <param name="budget">Time the deadline allows while the clock runs.</param>
        /// <returns>A registration to dispose when the runner no longer owns the source.</returns>
        /// <exception cref="ArgumentNullException">The source is null.</exception>
        public IDisposable Attach(CancellationTokenSource source, TimeSpan budget)
        {
            ArgumentNullException.ThrowIfNull(source);
            Entry entry = new Entry(this, source, budget);
            lock (_Lock)
            {
                entry.Started = _Time.GetTimestamp();
                if (_Pauses == 0) source.CancelAfter(budget);
                _Entries.Add(entry);
            }
            return entry;
        }

        /// <summary>Stop every attached deadline. Calls nest; each Pause needs a Resume.</summary>
        public void Pause()
        {
            lock (_Lock)
            {
                if (_Pauses++ > 0) return;
                long now = _Time.GetTimestamp();
                foreach (Entry entry in _Entries)
                {
                    entry.Remaining -= _Time.GetElapsedTime(entry.Started, now);
                    if (entry.Remaining < TimeSpan.Zero) entry.Remaining = TimeSpan.Zero;
                    try { entry.Source.CancelAfter(Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
                }
            }
        }

        /// <summary>Restart every attached deadline with the time it had left.</summary>
        public void Resume()
        {
            lock (_Lock)
            {
                if (_Pauses == 0 || --_Pauses > 0) return;
                long now = _Time.GetTimestamp();
                foreach (Entry entry in _Entries)
                {
                    entry.Started = now;
                    try { entry.Source.CancelAfter(entry.Remaining); } catch (ObjectDisposedException) { }
                }
            }
        }

        private void Detach(Entry entry) { lock (_Lock) { _Entries.Remove(entry); } }

        private sealed class Entry : IDisposable
        {
            private readonly InvocationClock _Owner;
            internal Entry(InvocationClock owner, CancellationTokenSource source, TimeSpan budget) { _Owner = owner; Source = source; Remaining = budget; }
            internal CancellationTokenSource Source { get; }
            internal TimeSpan Remaining { get; set; }
            internal long Started { get; set; }
            public void Dispose() { _Owner.Detach(this); }
        }
    }
}
