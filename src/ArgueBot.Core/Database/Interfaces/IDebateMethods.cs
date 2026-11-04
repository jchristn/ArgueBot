namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed database boundary for debate operations.</summary>
    public interface IDebateMethods
    {
        /// <summary>Operational admission limit, independent of historical roster sizes.</summary>
        int MaximumParticipants { get; set; }
        /// <summary>Read bounded proposal history with all recorded positions, including dissent from removed seats.</summary>
        Task<List<DebateProposal>> ReadProposalsAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default, int pageNumber = 1);
    }
}
