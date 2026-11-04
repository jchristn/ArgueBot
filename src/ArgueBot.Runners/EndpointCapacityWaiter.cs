namespace ArgueBot.Runners
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    internal sealed class EndpointCapacityWaiter
    {
        internal TaskCompletionSource<IDisposable> Ready { get; } = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LinkedListNode<EndpointCapacityWaiter>? Node { get; set; }
    }
}
