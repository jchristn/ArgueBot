namespace ArgueBot.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Typed session persistence operations with explicit ownership and cancellation.</summary>
    public interface ISessionMethods
    {
        /// <summary>Issue a one-hour local session after successful protected enrollment.</summary>
        Task<string> IssueLocalAsync(CancellationToken token = default);
        /// <summary>Exchange tenant-local password-equivalent material after transport TLS enforcement and rate limiting.</summary>
        Task<string> LoginAsync(LoginRequest request, string correlationId = "", CancellationToken token = default);
        /// <summary>Resolve an active session and its current active user and tenant.</summary>
        Task<RequestContext?> AuthenticateAsync(string secret, CancellationToken token = default);
        /// <summary>Revoke a session immediately.</summary>
        Task RevokeAsync(string secret, CancellationToken token = default);
    }
}
