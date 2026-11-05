namespace ArgueBot.Core.Database.Interfaces
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Tenants, users and bearer-token credentials. Without RBAC, access follows the bypass flags:
    /// system administrators manage tenants; tenant administrators manage users and credentials in their tenant;
    /// every user manages their own profile, password and credentials.
    /// </summary>
    public interface IIdentityMethods
    {
        /// <summary>List every tenant. System administrators only.</summary>
        Task<List<Tenant>> TenantsAsync(RequestContext context, CancellationToken token = default);
        /// <summary>Read a tenant: your own, or any tenant for a system administrator.</summary>
        Task<Tenant> TenantAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Create a tenant. System administrators only.</summary>
        Task<Tenant> CreateTenantAsync(RequestContext context, TenantRequest request, CancellationToken token = default);
        /// <summary>Rename, enable or disable a tenant with its expected revision. System administrators only; protected tenants cannot be disabled.</summary>
        Task<Tenant> UpdateTenantAsync(RequestContext context, string id, TenantRequest request, CancellationToken token = default);
        /// <summary>Delete a tenant and everything in it. Requires the tenant's exact name as confirmation; protected tenants cannot be deleted.</summary>
        Task DeleteTenantAsync(RequestContext context, string id, string confirmName, CancellationToken token = default);

        /// <summary>List users in the caller's tenant. Tenant administrators only.</summary>
        Task<List<User>> UsersAsync(RequestContext context, CancellationToken token = default);
        /// <summary>Read a user in the caller's tenant: yourself, or anyone for a tenant administrator.</summary>
        Task<User> UserAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Create a user in the caller's tenant. Tenant administrators only; only system administrators may grant IsAdmin.</summary>
        Task<User> CreateUserAsync(RequestContext context, UserRequest request, CancellationToken token = default);
        /// <summary>Update a user's names, email and flags with the expected revision. Users may update their own names and email; flags need a tenant administrator.</summary>
        Task<User> UpdateUserAsync(RequestContext context, string id, UserRequest request, CancellationToken token = default);
        /// <summary>Delete a user and their credentials and sessions. Their conversations and agents stay in the tenant, credited to the administrator. Tenant administrators only; protected users and yourself cannot be deleted.</summary>
        Task DeleteUserAsync(RequestContext context, string id, CancellationToken token = default);
        /// <summary>Set a password. Your own needs the current password; a tenant administrator can reset another user's. Revokes the user's other sessions.</summary>
        Task SetPasswordAsync(RequestContext context, string id, PasswordChangeRequest request, CancellationToken token = default);

        /// <summary>List a user's credentials: your own, or anyone's in your tenant for a tenant administrator.</summary>
        Task<List<AuthCredential>> CredentialsAsync(RequestContext context, string userId, CancellationToken token = default);
        /// <summary>Create a bearer-token credential. The token is returned only in this response.</summary>
        Task<AuthCredentialCreated> CreateCredentialAsync(RequestContext context, string userId, AuthCredentialRequest request, CancellationToken token = default);
        /// <summary>Rename, enable or disable a credential with its expected revision. Protected credentials cannot be disabled.</summary>
        Task<AuthCredential> UpdateCredentialAsync(RequestContext context, string userId, string id, AuthCredentialRequest request, CancellationToken token = default);
        /// <summary>Delete a credential and its sessions. Protected credentials cannot be deleted.</summary>
        Task DeleteCredentialAsync(RequestContext context, string userId, string id, CancellationToken token = default);

        /// <summary>List a user's active sessions: your own, or anyone's in your tenant for a tenant administrator. The caller's session is marked Current.</summary>
        Task<List<AuthSession>> SessionsAsync(RequestContext context, string userId, CancellationToken token = default);
        /// <summary>Revoke one session: your own, or anyone's in your tenant for a tenant administrator.</summary>
        Task RevokeSessionAsync(RequestContext context, string id, CancellationToken token = default);

        /// <summary>Search audit records newest first. Tenant administrators see their tenant; system administrators see every tenant and unauthenticated events.</summary>
        Task<AuditPage> AuditAsync(RequestContext context, AuditQuery query, CancellationToken token = default);
        /// <summary>Delete audit records older than a UTC cutoff, for retention. Returns the number deleted.</summary>
        Task<int> DeleteAuditAsync(RequestContext context, DateTime beforeUtc, CancellationToken token = default);
    }
}
