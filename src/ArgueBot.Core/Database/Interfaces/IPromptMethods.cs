namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Debate prompt library. Factory prompts are visible to every workspace and are read-only.</summary>
    public interface IPromptMethods
    {
        /// <summary>List factory and workspace prompts, optionally for one purpose, factory defaults first.</summary>
        Task<List<PromptTemplate>> EnumerateAsync(RequestContext context, string purpose = "", CancellationToken token = default);

        /// <summary>Read one visible prompt.</summary>
        Task<PromptTemplate> ReadAsync(RequestContext context, string id, CancellationToken token = default);

        /// <summary>Create a workspace prompt after validating its placeholders.</summary>
        Task<PromptTemplate> CreateAsync(RequestContext context, PromptTemplate prompt, CancellationToken token = default);

        /// <summary>Update a workspace prompt with its expected revision. Factory prompts and purposes cannot change.</summary>
        Task<PromptTemplate> UpdateAsync(RequestContext context, string id, PromptTemplate prompt, CancellationToken token = default);

        /// <summary>Copy any visible prompt, including a factory default, into a new editable workspace prompt.</summary>
        Task<PromptTemplate> DuplicateAsync(RequestContext context, string id, CancellationToken token = default);

        /// <summary>Delete a workspace prompt with its expected revision. Conversations using it fall back to the factory default; started debates keep their frozen copy.</summary>
        Task DeleteAsync(RequestContext context, string id, long revision, CancellationToken token = default);

        /// <summary>The prompt chosen for each purpose in a conversation; purposes without a choice use the factory default.</summary>
        Task<Dictionary<string, string>> SelectionAsync(RequestContext context, string conversationId, CancellationToken token = default);
    }
}
