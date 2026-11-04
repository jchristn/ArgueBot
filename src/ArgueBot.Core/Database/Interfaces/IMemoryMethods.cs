namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed database boundary for memory operations.</summary>
    public interface IMemoryMethods
    {
        /// <summary>Select original messages and the latest eligible summary; overflow never silently drops a source.</summary>
        Task<List<Message>> SelectAsync(RequestContext context, DebateRun run, RunnerProfile profile, int budget, int limit, bool compaction, CancellationToken token = default);
        /// <summary>Choose the largest uncompacted public or recipient-visible partition.</summary>
        Task<string> CompactionPartitionAsync(RequestContext context, DebateRun run, string recipientId, CancellationToken token = default);
        /// <summary>Return which of the supplied message IDs are pinned moderator constraints, in the order given.</summary>
        Task<List<string>> PinnedAsync(RequestContext context, string conversationId, IReadOnlyList<string> messageIds, CancellationToken token = default);
        /// <summary>Inspect a bounded summary page, including immutable original source identities.</summary>
        Task<List<ContextSummary>> EnumerateAsync(RequestContext context, string conversationId, string runId, int pageNumber = 1, CancellationToken token = default, string id = "", int sourcePageNumber = 1, string? recipientId = null, bool activeOnly = false);
        /// <summary>Exclude derived memory while preserving summaries and original sources for inspection.</summary>
        Task ExcludeAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default);
    }
}
