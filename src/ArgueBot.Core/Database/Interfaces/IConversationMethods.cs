namespace ArgueBot.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Domain-specific, tenant-scoped persistence boundary.</summary>
    public interface IConversationMethods
    {
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task<Conversation> CreateConversationAsync(RequestContext context, string title, CancellationToken token = default);
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task<Conversation> ReadConversationAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task<Page> EnumerateConversationsAsync(RequestContext context, string search = "", bool archived = false, int page = 1, int size = 25, CancellationToken token = default);
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task<Conversation> UpdateConversationAsync(RequestContext context, string id, ConversationRequest request, CancellationToken token = default);
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task DeleteConversationAsync(RequestContext context, string id, CancellationToken token = default);
    }
}
