namespace ArgueBot.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Typed run persistence operations with explicit ownership and cancellation.</summary>
    public interface IRunMethods
    {
        /// <summary>Create one run with immutable snapshots and an idempotency receipt.</summary>
        Task<DebateRun> CreateAsync(RequestContext context, string conversationId, CreateRunRequest request, CancellationToken token = default);
        /// <summary>List runs in an owned conversation.</summary>
        Task<List<DebateRun>> EnumerateAsync(RequestContext context, string conversationId, CancellationToken token = default);
        /// <summary>Read a run and its frozen participant configurations.</summary>
        Task<DebateRun> ReadAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default);
        /// <summary>Return true for an exact committed retry, or reject a reused key with different input.</summary>
        Task<bool> IsDuplicateCommandAsync(RequestContext context, string conversationId, string runId, CommandRequest request, CancellationToken token = default);
        /// <summary>Atomically save the run checkpoint, status event and moderator command receipt.</summary>
        Task SaveCommandAsync(RequestContext context, DebateRun run, CommandRequest request, CancellationToken token = default);
        /// <summary>Persist a checkpoint using the expected revision; emits a durable status event.</summary>
        Task SaveAsync(RequestContext context, DebateRun run, CancellationToken token = default);
    }
}
