namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Typed database boundary for credential operations.</summary>
    public interface ICredentialMethods
    {
        /// <summary>Read safe metadata without ciphertext or secret values.</summary>
        Task<List<CredentialMetadata>> EnumerateAsync(RequestContext context, CancellationToken token = default, string id = "", int pageNumber = 1, int pageSize = 100);
        /// <summary>Encrypt and revision-check a service-owned credential write. Never expose this operation as a plaintext read API.</summary>
        Task<CredentialMetadata> SaveAsync(RequestContext context, string id, CredentialRequest request, Func<string, string, string> encrypt, CancellationToken token);
        /// <summary>Read ciphertext for service-owned decryption; never expose this value over HTTP.</summary>
        Task<string> ResolveAsync(RequestContext context, string id, CancellationToken token, Action<CredentialMetadata>? resolved = null);
        /// <summary>Claim exclusive refresh responsibility using the expected credential revision.</summary>
        Task<long> ClaimRefreshAsync(RequestContext context, string id, long revision, CancellationToken token);
        /// <summary>Return a claimed login to ready when its refresh request provably did not reach, or was not processed by, the login service.</summary>
        Task ReleaseRefreshAsync(RequestContext context, string id, long revision, CancellationToken token);
        /// <summary>Atomically reencrypt stored records without changing credential identities, revisions or status.</summary>
        Task<long> ReencryptAsync(RequestContext context, Func<string, string, string> transform, CancellationToken token = default);
        /// <summary>Count stored values encrypted under a versioned key other than the named key, without reading secret values.</summary>
        Task<long> CountOtherKeysAsync(RequestContext context, string keyId, CancellationToken token = default);
        /// <summary>Visit every stored credential across the service in bounded batches with its safe name, status and ciphertext. Never expose the ciphertext.</summary>
        Task InspectAsync(Action<string, string, string, string> visit, CancellationToken token = default);
        /// <summary>Count stored credentials across the service that are not encrypted with the given key.</summary>
        Task<long> CountNotUsingKeyAsync(string keyId, CancellationToken token = default);
        /// <summary>Disable a credential without removing historical references.</summary>
        Task DisableAsync(RequestContext context, string id, long revision, CancellationToken token = default);
    }
}
