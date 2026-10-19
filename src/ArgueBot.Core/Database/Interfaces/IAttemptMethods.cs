namespace ArgueBot.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Service-owned attempt checkpoints. Execution callers must hold the conversation gate and exclusive service/database ownership.</summary>
    public interface IAttemptMethods
    {
        /// <summary>Atomically accept an answer, pending attempt, exact context provenance and agent-turn checkpoint. Expected run revision and recipient/context ownership are enforced.</summary>
        Task<GenerationWork> BeginAsync(RequestContext context, DebateRun run, IReadOnlyList<Message> selectedContext, string systemPrompt, int maxContextCharacters, int maxContextMessages, CancellationToken token = default);
        /// <summary>Durably mark invocation as about to start. A crash after this checkpoint leaves external execution uncertain; no automatic retry is permitted.</summary>
        Task MarkStreamingAsync(RequestContext context, GenerationAttempt attempt, CancellationToken token = default);
        /// <summary>Atomically finalize answer, attempt, successful directive consumption and the caller's next run checkpoint. An exact terminal retry has no effect.</summary>
        Task CompleteAsync(RequestContext context, DebateRun run, GenerationAttempt attempt, Message answer, CancellationToken token = default);
        /// <summary>Read a bounded newest-first page of attempts and their exact ordered context identifiers in an owned run.</summary>
        Task<AttemptPage> EnumerateAsync(RequestContext context, string conversationId, string runId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
    }
}
