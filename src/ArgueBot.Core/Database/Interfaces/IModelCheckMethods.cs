namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Durable, explicit model verification without automatic replay of uncertain provider work.</summary>
    public interface IModelCheckMethods
    {
        /// <summary>Admit once and freeze the exact configuration, or return the existing command result.</summary>
        Task<ModelCheck> CreateAsync(RequestContext context, string modelId, ModelCheckRequest request, CancellationToken token = default);
        /// <summary>Read a bounded newest-first page of check history.</summary>
        Task<List<ModelCheck>> EnumerateAsync(RequestContext context, string modelId, int pageNumber = 1, CancellationToken token = default);
        /// <summary>Record safe observed status without storing provider output.</summary>
        Task UpdateAsync(RequestContext context, ModelCheck check, CancellationToken token = default);
        /// <summary>Mark interrupted service-owned checks uncertain at startup, without retrying.</summary>
        Task ReconcileAsync(CancellationToken token = default);
    }
}
