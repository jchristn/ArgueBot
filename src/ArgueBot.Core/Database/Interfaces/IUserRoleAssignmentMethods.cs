namespace ArgueBot.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Tenant user role assignments with explicit resource scope and protected history.</summary>
    public interface IUserRoleAssignmentMethods
    {
        /// <summary>Enumerate assignments for a user inside the authenticated tenant.</summary>
        Task<UserRoleAssignmentPage> EnumerateAsync(RequestContext context, string userId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Read an assignment bound to its tenant and selected user.</summary>
        Task<UserRoleAssignmentRecord> ReadAsync(RequestContext context, string userId, string id, CancellationToken token = default);
        /// <summary>Create an explicit tenant/resource-scoped role assignment.</summary>
        Task<UserRoleAssignmentRecord> CreateAsync(RequestContext context, string userId, UserRoleAssignmentRequest request, CancellationToken token = default);
        /// <summary>Replace or deactivate an unprotected assignment under optimistic concurrency.</summary>
        Task<UserRoleAssignmentRecord> UpdateAsync(RequestContext context, string userId, string id, UserRoleAssignmentRequest request, CancellationToken token = default);
    }
}
