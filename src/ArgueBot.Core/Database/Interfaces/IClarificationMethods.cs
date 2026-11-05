namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed database boundary for clarification operations.</summary>
    public interface IClarificationMethods
    {
        /// <summary>Check all unresolved blocking requests without depending on a display page.</summary>
        Task<bool> HasBlockingAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default);
        /// <summary>Read a bounded page of one hundred requests, including answered requests.</summary>
        Task<List<ClarificationRequest>> EnumerateAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default, int pageNumber = 1);
    }
}
