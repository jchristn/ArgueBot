namespace ArgueBot.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Scoped role-permission mapping inspection and revision-checked active-set replacement.</summary>
    public interface IRolePermissionMethods
    {
        /// <summary>Enumerate active or inactive mappings with their current permission definitions.</summary>
        Task<RolePermissionPage> EnumerateAsync(RequestContext context, string roleId, bool active = true, int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Replace active custom-role mappings atomically while preserving historical mapping identities.</summary>
        Task<RoleRecord> ReplaceAsync(RequestContext context, string roleId, RolePermissionsRequest request, CancellationToken token = default);
    }
}
