namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed database boundary for interjection operations.</summary>
    public interface IInterjectionMethods
    {
        /// <summary>Persist context and invalidate stale agreement before any cancellation is requested.</summary>
        Task<InterjectionReceipt> CreateAsync(RequestContext context, string conversationId, InterjectionRequest request, CancellationToken token = default);
        /// <summary>Read a bounded newest-first page and exact participant deliveries from successful attempts.</summary>
        Task<List<InterjectionReceipt>> EnumerateAsync(RequestContext context, string conversationId, int pageNumber = 1, CancellationToken token = default);
    }
}
