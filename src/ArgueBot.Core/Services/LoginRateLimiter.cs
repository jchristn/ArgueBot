namespace ArgueBot.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>Counts failed sign-ins per key (source address or email) in a sliding window and blocks a key that exceeds the limit.</summary>
    public sealed class LoginRateLimiter
    {
        private readonly Dictionary<string, Queue<DateTime>> _Failures = new Dictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
        private readonly object _Lock = new object();
        private readonly int _Limit;
        private readonly TimeSpan _Window;
        private readonly TimeProvider _Clock;

        /// <summary>Allow <paramref name="limit"/> failures per key within <paramref name="window"/>.</summary>
        public LoginRateLimiter(int limit = 10, TimeSpan? window = null, TimeProvider? clock = null)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
            _Limit = limit; _Window = window ?? TimeSpan.FromMinutes(5); _Clock = clock ?? TimeProvider.System;
        }

        /// <summary>Throw 429 rate_limited when any key is blocked. The exception message carries the seconds to wait.</summary>
        public void Check(params string[] keys)
        {
            ArgumentNullException.ThrowIfNull(keys);
            DateTime now = _Clock.GetUtcNow().UtcDateTime; int wait = 0;
            lock (_Lock)
            {
                foreach (string key in keys.Where(item => item.Length > 0))
                {
                    if (!_Failures.TryGetValue(key, out Queue<DateTime>? failures)) continue;
                    Trim(failures, now);
                    if (failures.Count >= _Limit) wait = Math.Max(wait, (int)Math.Ceiling((failures.Peek() + _Window - now).TotalSeconds));
                }
            }
            if (wait > 0) throw new RateLimitedException(Math.Max(1, wait));
        }

        /// <summary>Record a failed attempt for each key.</summary>
        public void Fail(params string[] keys)
        {
            ArgumentNullException.ThrowIfNull(keys);
            DateTime now = _Clock.GetUtcNow().UtcDateTime;
            lock (_Lock)
            {
                if (_Failures.Count > 10000) foreach (string stale in _Failures.Where(pair => { Trim(pair.Value, now); return pair.Value.Count == 0; }).Select(pair => pair.Key).ToList()) _Failures.Remove(stale);
                foreach (string key in keys.Where(item => item.Length > 0))
                {
                    if (!_Failures.TryGetValue(key, out Queue<DateTime>? failures)) { failures = new Queue<DateTime>(); _Failures.Add(key, failures); }
                    Trim(failures, now); failures.Enqueue(now);
                }
            }
        }

        /// <summary>Forget failures for a key after a successful sign-in.</summary>
        public void Succeed(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            lock (_Lock) _Failures.Remove(key);
        }

        private void Trim(Queue<DateTime> failures, DateTime now) { while (failures.Count > 0 && failures.Peek() + _Window <= now) failures.Dequeue(); }
    }
}
