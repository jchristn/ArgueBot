namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Time-bound public links to conversation transcripts.</summary>
    public interface IShareMethods
    {
        /// <summary>List a conversation's links, newest first, including revoked and expired ones.</summary>
        Task<List<ConversationShare>> ListAsync(RequestContext context, string conversationId, CancellationToken token = default);
        /// <summary>Create a link. The returned token is shown only this once; <paramref name="baseUrl"/> builds the absolute URL.</summary>
        Task<ConversationShareCreated> CreateAsync(RequestContext context, string conversationId, ConversationShareRequest request, string baseUrl, CancellationToken token = default);
        /// <summary>Revoke a link so it stops working immediately.</summary>
        Task RevokeAsync(RequestContext context, string conversationId, string shareId, CancellationToken token = default);
        /// <summary>Read the transcript behind a link token, without a signed-in user. Unknown, expired, revoked and disabled-tenant links all throw 404 share_not_found.</summary>
        Task<SharedTranscript> ReadAsync(string shareToken, CancellationToken token = default);
    }
}
