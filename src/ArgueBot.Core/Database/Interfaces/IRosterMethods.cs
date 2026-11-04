namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed database boundary for roster operations.</summary>
    public interface IRosterMethods
    {
        /// <summary>Queue a reviewed roster mutation without rewriting an in-flight participant or historical votes.</summary>
        Task QueueAsync(RequestContext context, string conversationId, string runId, RosterChangeRequest request, CancellationToken token = default);
        /// <summary>Apply accepted roster changes at a safe execution boundary.</summary>
        Task ApplyPendingAsync(RequestContext context, DebateRun run, CancellationToken token);
        /// <summary>Read all seat membership intervals, including removed agents and their names.</summary>
        Task<List<DebateSeat>> EnumerateAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default);
    }
}
