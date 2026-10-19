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
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task<Message> AppendMessageAsync(RequestContext context, Message message, string commandKey = "", CancellationToken token = default);
        /// <summary>Perform the documented domain operation in the authenticated ownership scope.</summary>
        Task UpdateMessageAsync(RequestContext context, Message message, string delta, CancellationToken token = default);
    }
}
