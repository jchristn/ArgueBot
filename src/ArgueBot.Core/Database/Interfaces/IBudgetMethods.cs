namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Service-owned budget reservation boundary.</summary>
    public interface IBudgetMethods
    {
        /// <summary>Reserve a tool call exactly once before it executes.</summary>
        Task ReserveToolAsync(RequestContext context, string runId, string attemptId, string callId, CancellationToken token = default);
    }
}
