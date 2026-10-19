namespace ArgueBot.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Tenant-scoped identity administration with protected records and atomic security accounting.</summary>
    public interface IUserMethods
    {
        /// <summary>Check current active user/tenant execution authority for previously accepted service-owned work, independent of its issuing session lifetime.</summary>
        Task<bool> CanExecuteAsync(RequestContext context, CancellationToken token = default);
        /// <summary>Enumerate tenant identities for an administrator.</summary>
        Task<UserPage> EnumerateAsync(RequestContext context, string search = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Read identity metadata. Users may read their own record.</summary>
        Task<UserRecord> ReadAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Create a tenant identity with an optional password login.</summary>
        Task<UserRecord> CreateAsync(RequestContext context, UserRequest request, CancellationToken token = default);
        /// <summary>Replace identity metadata under optimistic concurrency and revoke sessions after password changes or disablement.</summary>
        Task<UserRecord> UpdateAsync(RequestContext context, string id, UserRequest request, CancellationToken token = default);
    }
}
