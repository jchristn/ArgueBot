namespace ArgueBot.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Models;

    /// <summary>Domain-specific, tenant-scoped persistence boundary.</summary>
    public interface IEventMethods
    {
        /// <summary>Read a bounded contiguous committed suffix. Negative/future cursors fail, and expired nonzero cursors return event_history_gap. Zero bootstraps retained replay.</summary>
        Task<List<DurableEvent>> ReadEventsAsync(RequestContext context, string conversationId, long after = 0, int pageSize = 1000, CancellationToken token = default);
        /// <summary>Service-only maintenance: discard event prefixes by UTC age/count, preserving at least the latest event for monotonic sequencing. Batches are one through one thousand conversations. Domain history and audit records are unchanged.</summary>
        Task<long> PruneEventsAsync(DateTime cutoffUtc, int maxEventsPerConversation, int batchSize = 100, CancellationToken token = default);
    }
}
