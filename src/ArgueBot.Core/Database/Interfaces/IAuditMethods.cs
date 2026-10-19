namespace ArgueBot.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Append-only security accounting. No update/delete operation is exposed.</summary>
    public interface IAuditMethods
    {
        /// <summary>Page tenant security events as a tenant administrator, or all events as a system administrator. Optional operation matches exactly; page size is one through one hundred.</summary>
        Task<AuditPage> EnumerateAsync(RequestContext context, string operation = "", int pageNumber = 1, int pageSize = 25, CancellationToken token = default);
        /// <summary>Record a denied HTTP request independently of request capture. Only status 401/403 is accepted. Call from the trusted service pipeline, with validated context only.</summary>
        Task RecordDeniedRequestAsync(RequestContext context, int status, CancellationToken token = default);
    }
}
