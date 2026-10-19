namespace ArgueBot.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Typed profile persistence operations with explicit ownership and cancellation.</summary>
    public interface IProfileMethods
    {
        /// <summary>Create a profile after validating protocol and resource limits.</summary>
        Task<RunnerProfile> CreateAsync(RequestContext context, RunnerProfile profile, CancellationToken token = default);
        /// <summary>List enabled or disabled current profiles. Historical snapshots remain private to their runs.</summary>
        Task<List<RunnerProfile>> EnumerateAsync(RequestContext context, CancellationToken token = default);
        /// <summary>Read a profile in the caller's ownership scope.</summary>
        Task<RunnerProfile> ReadAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Update a reusable profile. Historical run snapshots are never changed.</summary>
        Task<RunnerProfile> UpdateAsync(RequestContext context, string id, RunnerProfile profile, CancellationToken token = default);
        /// <summary>Delete a reusable profile while keeping immutable historical snapshots.</summary>
        Task DeleteAsync(RequestContext context, string id, CancellationToken token = default);
    }
}
