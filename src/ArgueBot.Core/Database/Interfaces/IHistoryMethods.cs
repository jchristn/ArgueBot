namespace ArgueBot.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Typed history persistence operations with explicit ownership and cancellation.</summary>
    public interface IHistoryMethods
    {
        /// <summary>Persist redacted diagnostics. The caller must apply the capture policy before invoking this method.</summary>
        Task InsertAsync(RequestHistoryEntry entry, CancellationToken token = default);
        /// <summary>Enumerate metadata with backend filtering and exact totals. Body/header data is loaded only by detail.</summary>
        Task<HistoryPage> EnumerateAsync(RequestContext context, HistoryFilter filter, CancellationToken token = default);
        /// <summary>Read an authorized diagnostic detail including redacted body prefixes and headers.</summary>
        Task<RequestHistoryEntry> ReadAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Delete one authorized request capture.</summary>
        Task DeleteAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Prune records before a UTC cutoff. Only a service maintenance task should call this.</summary>
        Task PruneAsync(DateTime cutoffUtc, CancellationToken token = default);
        /// <summary>Return every time bucket, including empty buckets, for a bounded UTC range.</summary>
        Task<List<ActivityBucket>> SummaryAsync(RequestContext context, DateTime fromUtc, DateTime toUtc, int bucketMinutes, CancellationToken token = default);
    }
}
