namespace ArgueBot.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Protected global permission inspection and tenant custom permission lifecycle.</summary>
    public interface IPermissionMethods
    {
        /// <summary>Enumerate visible permission definitions with literal name search.</summary>
        Task<PermissionPage> EnumerateAsync(RequestContext context, string search = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Read a current-tenant or protected global permission.</summary>
        Task<PermissionRecord> ReadAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Create a tenant permission with validated resources, operations and permit/deny semantics.</summary>
        Task<PermissionRecord> CreateAsync(RequestContext context, PermissionRequest request, CancellationToken token = default);
        /// <summary>Replace a custom definition under optimistic concurrency.</summary>
        Task<PermissionRecord> UpdateAsync(RequestContext context, string id, PermissionRequest request, CancellationToken token = default);
    }
}
