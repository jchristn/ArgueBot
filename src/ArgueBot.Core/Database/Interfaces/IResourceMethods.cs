namespace ArgueBot.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Tenant resource library: named markdown documents with revision history. Every member of a tenant shares its library.</summary>
    /// <remarks>Methods throw <see cref="ArgueBot.Core.Services.DomainException"/> with 401 unauthenticated, 404 resource_not_found, 409 revision_conflict, 409 resource_exists, 409 resource_locked (agent writes) and 400 or 413 validation codes.</remarks>
    public interface IResourceMethods
    {
        /// <summary>Page resource summaries in name order, filtered by a literal case-insensitive search of name, title and description, and by tag.</summary>
        /// <param name="context">Signed-in principal.</param>
        /// <param name="search">Text to find in name, title or description; empty matches all.</param>
        /// <param name="tag">Only resources with this tag; empty matches all.</param>
        /// <param name="pageNumber">One-based page number.</param>
        /// <param name="pageSize">1 through 200 resources per page.</param>
        /// <param name="token">Cancellation token.</param>
        Task<ResourcePage> EnumerateAsync(RequestContext context, string search = "", string tag = "", int pageNumber = 1, int pageSize = 50, CancellationToken token = default);

        /// <summary>Every resource summary in the tenant, in name order.</summary>
        Task<List<ResourceSummary>> SummariesAsync(RequestContext context, CancellationToken token = default);

        /// <summary>Read a resource by ID.</summary>
        Task<Resource> ReadAsync(RequestContext context, string id, CancellationToken token = default);

        /// <summary>Read a resource by name, case-insensitively.</summary>
        Task<Resource> ReadByNameAsync(RequestContext context, string name, CancellationToken token = default);

        /// <summary>Create a resource as revision 1.</summary>
        /// <param name="context">Signed-in principal.</param>
        /// <param name="request">Name (required), title, description, tags, body and locked.</param>
        /// <param name="author">The agent creating it, or null for the signed-in user. Agents cannot create locked resources.</param>
        /// <param name="token">Cancellation token.</param>
        Task<Resource> CreateAsync(RequestContext context, ResourceRequest request, ResourceAuthor? author = null, CancellationToken token = default);

        /// <summary>Change a resource with its expected revision. Null fields keep their values; a new name renames it.</summary>
        /// <param name="context">Signed-in principal.</param>
        /// <param name="id">Resource ID.</param>
        /// <param name="request">Fields to change and the expected revision.</param>
        /// <param name="author">The agent changing it, or null for the signed-in user. Agents cannot change locked resources, rename them or change the locked flag.</param>
        /// <param name="token">Cancellation token.</param>
        Task<Resource> UpdateAsync(RequestContext context, string id, ResourceRequest request, ResourceAuthor? author = null, CancellationToken token = default);

        /// <summary>Append text to the end of a resource's body without a revision check, for running notes. A newline separates it from existing text.</summary>
        Task<Resource> AppendAsync(RequestContext context, string id, string text, ResourceAuthor? author = null, CancellationToken token = default);

        /// <summary>Delete a resource and its history with its expected revision.</summary>
        Task DeleteAsync(RequestContext context, string id, long revision, CancellationToken token = default);

        /// <summary>Saved revisions of a resource, newest first, without bodies.</summary>
        Task<List<ResourceRevision>> RevisionsAsync(RequestContext context, string id, CancellationToken token = default);

        /// <summary>Read one saved revision with its body.</summary>
        Task<ResourceRevision> ReadRevisionAsync(RequestContext context, string id, long revision, CancellationToken token = default);

        /// <summary>Restore a saved revision's title, description, tags and body as a new revision.</summary>
        Task<Resource> RevertAsync(RequestContext context, string id, ResourceRevertRequest request, CancellationToken token = default);

        /// <summary>Import markdown files in one transaction.</summary>
        Task<ResourceImportResult> ImportAsync(RequestContext context, ResourceImportRequest request, CancellationToken token = default);

        /// <summary>Literal case-insensitive search of name, title, description and body.</summary>
        /// <param name="context">Signed-in principal.</param>
        /// <param name="query">Text to find, 1 through 4000 characters.</param>
        /// <param name="visibleIds">Only these resource IDs, or null for every resource.</param>
        /// <param name="limit">Most results, 1 through 50.</param>
        /// <param name="token">Cancellation token.</param>
        Task<List<ResourceSearchHit>> SearchAsync(RequestContext context, string query, ICollection<string>? visibleIds = null, int limit = 20, CancellationToken token = default);
    }
}
