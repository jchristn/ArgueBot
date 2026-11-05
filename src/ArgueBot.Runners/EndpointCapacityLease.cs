namespace ArgueBot.Runners
{
    using System;
    using System.Threading;

    internal sealed class EndpointCapacityLease : IDisposable
    {
        private Action? _Release;
        internal EndpointCapacityLease(Action release) { _Release = release; }
        public void Dispose() { Interlocked.Exchange(ref _Release, null)?.Invoke(); }
    }
}
