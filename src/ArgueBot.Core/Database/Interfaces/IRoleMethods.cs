namespace ArgueBot.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Protected global role inspection and tenant custom-role lifecycle.</summary>
    public interface IRoleMethods
    {
        /// <summary>Enumerate visible built-ins and current-tenant custom roles.</summary>
        Task<RolePage> EnumerateAsync(RequestContext context, string search = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Read one visible role.</summary>
        Task<RoleRecord> ReadAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Create a tenant custom role, optionally cloning an accessible definition.</summary>
        Task<RoleRecord> CreateAsync(RequestContext context, RoleRequest request, CancellationToken token = default);
        /// <summary>Update or deactivate a custom role under optimistic concurrency. Built-ins are immutable.</summary>
        Task<RoleRecord> UpdateAsync(RequestContext context, string id, RoleRequest request, CancellationToken token = default);
    }
}
