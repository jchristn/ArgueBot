namespace Test.Shared
{
    using System;

    /// <summary>A clock that moves only when a test advances it.</summary>
    internal sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _Now;
        internal ManualClock(DateTimeOffset start) { _Now = start; }
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() { return _Now; }
        internal void Advance(TimeSpan by) { _Now += by; }
    }
}
