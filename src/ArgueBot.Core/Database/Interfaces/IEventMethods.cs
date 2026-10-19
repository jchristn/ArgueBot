namespace ArgueBot.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Domain-specific, tenant-scoped persistence boundary.</summary>
    public interface IEventMethods
    {
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task<List<DurableEvent>> ReadEventsAsync(RequestContext context, string conversationId, long after = 0, CancellationToken token = default);
    }
}
