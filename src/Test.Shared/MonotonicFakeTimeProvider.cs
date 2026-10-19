namespace Test.Shared
{
    using System;
    using System.Threading;
    using Microsoft.Extensions.Time.Testing;

    internal sealed class MonotonicFakeTimeProvider : FakeTimeProvider
    {
        private long _WallOffsetTicks;
        internal void AdjustWallClock(TimeSpan difference) { Interlocked.Add(ref _WallOffsetTicks, difference.Ticks); }
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() { return base.GetUtcNow().AddTicks(Interlocked.Read(ref _WallOffsetTicks)); }
        /// <inheritdoc />
        public override long GetTimestamp() { return base.GetUtcNow().Ticks; }
    }
}
