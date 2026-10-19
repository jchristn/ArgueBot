namespace ArgueBot.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Domain-specific, tenant-scoped persistence boundary.</summary>
    public interface IMessageMethods
    {
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task<List<Message>> ReadMessagesAsync(RequestContext context, string conversationId, CancellationToken token = default);
        /// <summary>Prepare completed public/recipient context using bounded metadata and payload reads. Character budget is remaining UTF-16 content capacity (zero through 16777216), and message limit is one through 10000. Oversized context fails without truncation.</summary>
        Task<List<Message>> ReadGenerationContextAsync(RequestContext context, string conversationId, string participantId, int characterBudget, int maxMessages, CancellationToken token = default);
        /// <summary>Recognize a committed message retry including its original run and private recipient.</summary>
        Task<bool> IsDuplicateMessageAsync(RequestContext context, Message message, string commandKey, CancellationToken token = default);
        /// <summary>Append a message with an optional atomic command receipt.</summary>
        Task<Message> AppendMessageAsync(RequestContext context, Message message, string commandKey = "", CancellationToken token = default);
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task UpdateMessageAsync(RequestContext context, Message message, string delta, CancellationToken token = default);
    }
}
