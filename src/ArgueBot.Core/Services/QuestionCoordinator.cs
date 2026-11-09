namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Holds live ask_user calls open until the user resolves the request, the hold window ends or the turn must stop. Only in-memory waiting lives here; request state is in the database.</summary>
    public sealed class QuestionCoordinator
    {
        /// <summary>The request was answered, skipped, expired or cancelled while the call was held.</summary>
        public const string Resolved = "resolved";
        /// <summary>The call returned without an answer; the turn ends and resumes once the request is resolved.</summary>
        public const string Parked = "parked";

        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _Waiters = new ConcurrentDictionary<string, TaskCompletionSource<string>>(StringComparer.Ordinal);
        private readonly TimeProvider _Time;
        private int _MaxHolds;

        /// <summary>Create a coordinator.</summary>
        /// <param name="maxHolds">Most calls held at once, 0 through 64. Above it, new waiting requests park at once.</param>
        /// <param name="time">Time source; the system clock when null.</param>
        /// <exception cref="ArgumentOutOfRangeException">maxHolds is outside 0 through 64.</exception>
        public QuestionCoordinator(int maxHolds = 4, TimeProvider? time = null)
        {
            MaxHolds = maxHolds;
            _Time = time ?? TimeProvider.System;
        }

        /// <summary>Most calls held at once, 0 through 64. Default 4.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 through 64.</exception>
        public int MaxHolds
        {
            get { return _MaxHolds; }
            set { if (value < 0 || value > 64) throw new ArgumentOutOfRangeException(nameof(value), "Use 0 through 64 held questions."); _MaxHolds = value; }
        }

        /// <summary>Calls currently held.</summary>
        public int Held { get { return _Waiters.Count; } }

        /// <summary>Whether a call for the request is held.</summary>
        /// <param name="requestId">Request identifier.</param>
        /// <returns>True while a call waits.</returns>
        public bool IsHeld(string requestId) { ArgumentNullException.ThrowIfNull(requestId); return _Waiters.ContainsKey(requestId); }

        /// <summary>Wait for the request to be resolved, for at most the hold window.</summary>
        /// <param name="requestId">Request identifier.</param>
        /// <param name="hold">Hold window; zero parks at once.</param>
        /// <param name="token">Cancelled when the turn is interrupted or stopped.</param>
        /// <returns><see cref="Resolved"/> or <see cref="Parked"/>.</returns>
        /// <exception cref="OperationCanceledException">The turn was cancelled while waiting.</exception>
        public async Task<string> WaitAsync(string requestId, TimeSpan hold, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(requestId);
            if (hold <= TimeSpan.Zero || _Waiters.Count >= _MaxHolds) return Parked;
            TaskCompletionSource<string> waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_Waiters.TryAdd(requestId, waiter)) return Parked;
            try
            {
                using (CancellationTokenSource window = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    Task delay = Task.Delay(hold, _Time, window.Token);
                    Task finished = await Task.WhenAny(waiter.Task, delay).ConfigureAwait(false);
                    window.Cancel();
                    if (finished == waiter.Task) return await waiter.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return Parked;
                }
            }
            finally { _Waiters.TryRemove(requestId, out TaskCompletionSource<string>? _); }
        }

        /// <summary>Wake a held call because its request was resolved.</summary>
        /// <param name="requestId">Request identifier.</param>
        /// <returns>True when a held call was woken.</returns>
        public bool Notify(string requestId) { return Complete(requestId, Resolved); }

        /// <summary>Return a held call without an answer so its turn ends now and resumes after the answer.</summary>
        /// <param name="requestId">Request identifier.</param>
        /// <returns>True when a held call was released.</returns>
        public bool Park(string requestId) { return Complete(requestId, Parked); }

        private bool Complete(string requestId, string outcome)
        {
            ArgumentNullException.ThrowIfNull(requestId);
            return _Waiters.TryGetValue(requestId, out TaskCompletionSource<string>? waiter) && waiter.TrySetResult(outcome);
        }
    }
}
