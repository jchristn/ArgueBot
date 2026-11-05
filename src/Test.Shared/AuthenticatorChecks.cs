namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Session lifetime, refresh, revocation, tampering, credential expiry, tenant choice and rate limiting with a controlled clock, on any database driver.</summary>
    internal static class AuthenticatorChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, CancellationToken token)
        {
            ManualClock clock = new ManualClock(DateTimeOffset.UtcNow);
            RequestAuthenticator authenticator = new RequestAuthenticator(database, new SessionProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), new LoginRateLimiter(clock: clock), clock);
            RequestContext received = new RequestContext { CorrelationId = "req_fixture", SourceIp = "127.0.0.1" };

            // A session expires after its lifetime.
            TokenResponse first = await authenticator.LoginAsync(new LoginRequest { Email = IdentitySeed.Email, Password = IdentitySeed.Password }, received, "PasswordBody", "fixture", token).ConfigureAwait(false);
            RequestContext signedIn = await authenticator.AuthenticateAsync(first.Token, "Bearer", received, token).ConfigureAwait(false);
            Require(signedIn.IsAuthenticated && signedIn.IsAdmin && signedIn.IsTenantAdmin && signedIn.TenantId == IdentitySeed.TenantId && signedIn.SessionId == first.SessionId && signedIn.PrincipalType == "User", "A session token must authenticate as the default administrator.");
            clock.Advance(RequestAuthenticator.SessionLifetime + TimeSpan.FromSeconds(1));
            await Reject(() => authenticator.AuthenticateAsync(first.Token, "Bearer", received, token), "session_expired").ConfigureAwait(false);

            // Refresh gives a fresh lifetime and retires the previous token; sign-out revokes the session.
            TokenResponse second = await authenticator.LoginAsync(new LoginRequest { Email = IdentitySeed.Email, Password = IdentitySeed.Password }, received, "PasswordBody", "fixture", token).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMinutes(45));
            RequestContext beforeRefresh = await authenticator.AuthenticateAsync(second.Token, "Bearer", received, token).ConfigureAwait(false);
            TokenResponse refreshed = await authenticator.RefreshAsync(beforeRefresh, token).ConfigureAwait(false);
            await Reject(() => authenticator.AuthenticateAsync(second.Token, "Bearer", received, token), "invalid_token").ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMinutes(45));
            RequestContext afterRefresh = await authenticator.AuthenticateAsync(refreshed.Token, "XToken", received, token).ConfigureAwait(false);
            Require(afterRefresh.AuthScheme == "XToken", "The carrier must be recorded on the request context.");
            await authenticator.LogoutAsync(afterRefresh, token).ConfigureAwait(false);
            await Reject(() => authenticator.AuthenticateAsync(refreshed.Token, "Bearer", received, token), "session_revoked").ConfigureAwait(false);

            // Tampered and foreign tokens fail closed.
            TokenResponse third = await authenticator.LoginAsync(new LoginRequest { Email = IdentitySeed.Email, Password = IdentitySeed.Password }, received, "PasswordBody", "fixture", token).ConfigureAwait(false);
            char[] flipped = third.Token.ToCharArray(); flipped[10] = flipped[10] == 'A' ? 'B' : 'A';
            await Reject(() => authenticator.AuthenticateAsync(new string(flipped), "Bearer", received, token), "invalid_token").ConfigureAwait(false);
            RequestAuthenticator foreign = new RequestAuthenticator(database, new SessionProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), clock: clock);
            await Reject(() => foreign.AuthenticateAsync(third.Token, "Bearer", received, token), "invalid_token").ConfigureAwait(false);

            // The default bearer token authenticates directly as a credential.
            RequestContext credential = await authenticator.AuthenticateAsync(IdentitySeed.Token, "Bearer", received, token).ConfigureAwait(false);
            Require(credential.PrincipalType == "Credential" && credential.CredentialId == IdentitySeed.CredentialId && credential.SessionId.Length == 0, "The default bearer token must authenticate as its credential.");
            await Reject(() => authenticator.RefreshAsync(credential, token), "not_refreshable").ConfigureAwait(false);

            // An expiring credential stops at its expiry.
            RequestContext system = new RequestContext { TenantId = IdentitySeed.TenantId, UserId = IdentitySeed.UserId, IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true };
            AuthCredentialCreated expiring = await database.Identity.CreateCredentialAsync(system, IdentitySeed.UserId, new AuthCredentialRequest { Name = "Expiring fixture", ExpiresUtc = clock.GetUtcNow().UtcDateTime.AddHours(2) }, token).ConfigureAwait(false);
            await authenticator.AuthenticateAsync(expiring.Token, "Bearer", received, token).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromHours(3));
            await Reject(() => authenticator.AuthenticateAsync(expiring.Token, "Bearer", received, token), "credential_expired").ConfigureAwait(false);
            await database.Identity.DeleteCredentialAsync(system, IdentitySeed.UserId, expiring.Credential.Id, token).ConfigureAwait(false);

            // The same email and password in two tenants needs a tenant choice; a disabled tenant ends its sessions.
            Tenant north = await database.Identity.CreateTenantAsync(system, new TenantRequest { Name = "North fixture" }, token).ConfigureAwait(false);
            Tenant south = await database.Identity.CreateTenantAsync(system, new TenantRequest { Name = "South fixture" }, token).ConfigureAwait(false);
            foreach (Tenant tenant in new[] { north, south })
                await database.Identity.CreateUserAsync(new RequestContext { TenantId = tenant.Id, UserId = IdentitySeed.UserId, IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true }, new UserRequest { FirstName = "Shared", Email = "shared@example.test", Password = "shared-password" }, token).ConfigureAwait(false);
            try { await authenticator.LoginAsync(new LoginRequest { Email = "shared@example.test", Password = "shared-password" }, received, "PasswordBody", "fixture", token).ConfigureAwait(false); throw new InvalidDataException("An ambiguous email signed in without a tenant."); }
            catch (TenantRequiredException ex) { Require(ex.Tenants.Select(item => item.Id).OrderBy(item => item, StringComparer.Ordinal).SequenceEqual(new[] { north.Id, south.Id }.OrderBy(item => item, StringComparer.Ordinal)), "The tenant choice must list both tenants."); }
            TokenResponse chosen = await authenticator.LoginAsync(new LoginRequest { Email = "shared@example.test", Password = "shared-password", TenantId = south.Id }, received, "PasswordBody", "fixture", token).ConfigureAwait(false);
            Require(chosen.Tenant.Id == south.Id, "Choosing a tenant must sign in to it.");
            Tenant current = await database.Identity.TenantAsync(system, south.Id, token).ConfigureAwait(false);
            await database.Identity.UpdateTenantAsync(system, south.Id, new TenantRequest { Name = current.Name, Active = false, Revision = current.Revision }, token).ConfigureAwait(false);
            await Reject(() => authenticator.AuthenticateAsync(chosen.Token, "Bearer", received, token), "tenant_inactive").ConfigureAwait(false);
            TokenResponse remaining = await authenticator.LoginAsync(new LoginRequest { Email = "shared@example.test", Password = "shared-password" }, received, "PasswordBody", "fixture", token).ConfigureAwait(false);
            Require(remaining.Tenant.Id == north.Id, "With one tenant disabled the other must be chosen without asking.");
            foreach (Tenant tenant in new[] { north, south }) await database.Identity.DeleteTenantAsync(system, tenant.Id, tenant.Name, token).ConfigureAwait(false);

            // Rate limiting counts failures per address and per email within the window.
            LoginRateLimiter limiter = new LoginRateLimiter(3, TimeSpan.FromMinutes(5), clock);
            RequestAuthenticator limited = new RequestAuthenticator(database, new SessionProtector(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), limiter, clock);
            RequestContext attacker = new RequestContext { CorrelationId = "req_limited", SourceIp = "198.51.100.7" };
            for (int attempt = 0; attempt < 3; attempt++)
                await Reject(() => limited.LoginAsync(new LoginRequest { Email = IdentitySeed.Email, Password = "wrong" }, attacker, "PasswordBody", "fixture", token), "invalid_credentials").ConfigureAwait(false);
            try { await limited.LoginAsync(new LoginRequest { Email = IdentitySeed.Email, Password = IdentitySeed.Password }, attacker, "PasswordBody", "fixture", token).ConfigureAwait(false); throw new InvalidDataException("A blocked address signed in."); }
            catch (RateLimitedException ex) { Require(ex.RetryAfterSeconds > 0 && ex.RetryAfterSeconds <= 300, "Retry-After must fall inside the window."); }
            // The email is blocked from other addresses too, then the window passes.
            await Reject(() => limited.LoginAsync(new LoginRequest { Email = IdentitySeed.Email, Password = IdentitySeed.Password }, new RequestContext { SourceIp = "203.0.113.9" }, "PasswordBody", "fixture", token), "rate_limited").ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMinutes(5));
            await limited.LoginAsync(new LoginRequest { Email = IdentitySeed.Email, Password = IdentitySeed.Password }, attacker, "PasswordBody", "fixture", token).ConfigureAwait(false);
        }

        private static async Task Reject(Func<Task> action, string code)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) when (ex.Code == code) { return; }
            catch (DomainException ex) { throw new InvalidDataException("Expected " + code + " but got " + ex.Code + ": " + ex.Message); }
            throw new InvalidDataException("Expected " + code + " but the call succeeded.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
