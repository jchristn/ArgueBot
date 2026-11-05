namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed database boundary for catalog operations.</summary>
    public interface ICatalogMethods
    {
        /// <summary>List reusable endpoint configuration, including disabled records.</summary>
        Task<List<EndpointProfile>> EndpointsAsync(RequestContext context, CancellationToken token = default, string id = "", int pageNumber = 1, int pageSize = 100);
        /// <summary>Create or revision-check an update to a reusable endpoint.</summary>
        Task<EndpointProfile> SaveEndpointAsync(RequestContext context, EndpointProfile item, CancellationToken token = default);
        /// <summary>List reusable model configuration, including disabled records.</summary>
        Task<List<ModelProfile>> ModelsAsync(RequestContext context, CancellationToken token = default, string id = "", int pageNumber = 1, int pageSize = 100);
        /// <summary>Create or revision-check an update to a reusable model.</summary>
        Task<ModelProfile> SaveModelAsync(RequestContext context, ModelProfile item, CancellationToken token = default);
        /// <summary>Resolve a reusable model at admission; subsequent snapshots retain these exact nonsecret settings.</summary>
        Task ResolveAsync(RequestContext context, RunnerProfile profile, CancellationToken token = default);
    }
}
