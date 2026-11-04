namespace ArgueBot.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class EndpointCapacityGate
    {
        private readonly object _Lock = new object();
        private readonly LinkedList<EndpointCapacityWaiter> _Queue = new LinkedList<EndpointCapacityWaiter>();
        private int _Active;
        private int _Capacity = 1;
        private long _Revision;
        internal async Task<IDisposable> EnterAsync(int capacity, long revision, CancellationToken token)
        {
            EndpointCapacityWaiter waiter = new EndpointCapacityWaiter();
            lock (_Lock)
            {
                token.ThrowIfCancellationRequested();
                if (revision >= _Revision) { _Capacity = capacity; _Revision = revision; }
                waiter.Node = _Queue.AddLast(waiter);
                Drain();
            }
            using (CancellationTokenRegistration registration = token.Register(() => Cancel(waiter, token)))
            {
                IDisposable lease = await waiter.Ready.Task.ConfigureAwait(false);
                if (token.IsCancellationRequested) { lease.Dispose(); token.ThrowIfCancellationRequested(); }
                return lease;
            }
        }
        private void Cancel(EndpointCapacityWaiter waiter, CancellationToken token)
        {
            lock (_Lock)
            {
                if (waiter.Node?.List == null) return;
                _Queue.Remove(waiter.Node);
                waiter.Ready.TrySetCanceled(token);
                Drain();
            }
        }
        private void Drain()
        {
            while (_Active < _Capacity && _Queue.First != null)
            {
                EndpointCapacityWaiter waiter = _Queue.First.Value;
                _Queue.RemoveFirst(); _Active++;
                waiter.Ready.SetResult(new EndpointCapacityLease(Release));
            }
        }
        private void Release() { lock (_Lock) { _Active--; Drain(); } }
    }
}
