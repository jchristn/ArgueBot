namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Signs users in, issues and refreshes revocable session tokens, and turns a bearer token (session or credential) into a request context.
    /// Every failure is a 401 with a stable code; nothing reveals whether an email exists.
    /// </summary>
    public sealed class RequestAuthenticator
    {
        /// <summary>Interactive session lifetime. A refresh issues a new token with a fresh lifetime.</summary>
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);
        private readonly DatabaseDriverBase _Database;
        private readonly SessionProtector _Protector;
        private readonly LoginRateLimiter _Limiter;
        private readonly TimeProvider _Clock;
        // Last-use updates are throttled in memory so a burst of parallel requests causes one write per principal per minute, not one each.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _Touched = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>Create an authenticator over the service database and its session key.</summary>
        public RequestAuthenticator(DatabaseDriverBase database, SessionProtector protector, LoginRateLimiter? limiter = null, TimeProvider? clock = null)
        {
            ArgumentNullException.ThrowIfNull(database); ArgumentNullException.ThrowIfNull(protector);
            _Database = database; _Protector = protector; _Limiter = limiter ?? new LoginRateLimiter(); _Clock = clock ?? TimeProvider.System;
        }

        /// <summary>Verify an email and password and issue a session token. Throws 401, 409 tenant_required or 429.</summary>
        public async Task<TokenResponse> LoginAsync(LoginRequest request, RequestContext received, string scheme, string userAgent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(received); ArgumentNullException.ThrowIfNull(scheme); ArgumentNullException.ThrowIfNull(userAgent);
            string email = request.Email.Trim();
            string ipKey = "ip:" + received.SourceIp; string emailKey = "email:" + email.ToLowerInvariant();
            _Limiter.Check(ipKey, emailKey);
            if (email.Length == 0 || request.Password.Length == 0) throw new DomainException(400, "credentials_required", "Enter an email and a password.", email.Length == 0 ? "email" : "password");
            List<LoginCandidate> candidates = await SessionStore.LoginCandidatesAsync(_Database, email, request.TenantId.Trim(), token).ConfigureAwait(false);
            // Always run the password function at least once so an unknown email takes as long as a wrong password.
            if (candidates.Count == 0) PasswordHasher.Create(request.Password, out _, out _, out _);
            List<LoginCandidate> verified = candidates.Where(candidate => candidate.Salt.Length > 0 && PasswordHasher.Verify(request.Password, candidate.Hash, candidate.Salt, candidate.Iterations)).ToList();
            if (verified.Count == 0)
            {
                _Limiter.Fail(ipKey, emailKey);
                await RecordAsync(Event(received, "AuthFailure", "failure", 401, "invalid_credentials", candidates.Count == 0 ? "NotFound" : "Invalid", "Email or password not accepted", scheme, candidates.Count == 1 ? candidates[0].User : null), token).ConfigureAwait(false);
                throw new DomainException(401, "invalid_credentials", "The email or password is not correct.");
            }
            List<LoginCandidate> usable = verified.Where(candidate => candidate.User.Active && candidate.Tenant.Active).ToList();
            if (usable.Count == 0)
            {
                _Limiter.Fail(ipKey, emailKey);
                await RecordAsync(Event(received, "AuthFailure", "failure", 401, "account_inactive", "Inactive", verified[0].Tenant.Active ? "User is disabled" : "Tenant is disabled", scheme, verified[0].User), token).ConfigureAwait(false);
                throw new DomainException(401, "account_inactive", verified[0].Tenant.Active ? "This account is disabled. Ask an administrator to enable it." : "This tenant is disabled. Ask a system administrator to enable it.");
            }
            if (usable.Count > 1) throw new TenantRequiredException(usable.Select(candidate => new TenantChoice { Id = candidate.Tenant.Id, Name = candidate.Tenant.Name }).ToList());
            _Limiter.Succeed(emailKey);
            LoginCandidate chosen = usable[0];
            TokenResponse response = await IssueAsync(chosen.User, chosen.Tenant, "", "User", scheme, received.SourceIp, userAgent, token).ConfigureAwait(false);
            AuditEvent success = Event(received, "SessionIssued", "success", 201, "", "Success", "Signed in with email and password", scheme, chosen.User);
            success.SessionId = response.SessionId; success.PrincipalType = "User";
            await RecordAsync(success, token).ConfigureAwait(false);
            return response;
        }

        /// <summary>Resolve a bearer token to a signed-in request context. Throws 401 when the token is unknown, expired, revoked or belongs to a disabled user, credential or tenant.</summary>
        public async Task<RequestContext> AuthenticateAsync(string bearer, string scheme, RequestContext received, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(bearer); ArgumentNullException.ThrowIfNull(scheme); ArgumentNullException.ThrowIfNull(received);
            DateTime now = _Clock.GetUtcNow().UtcDateTime;
            string hash = SecretTokens.Hash(bearer);
            StoredPrincipal? principal;
            if (bearer.StartsWith(SessionProtector.Prefix, StringComparison.Ordinal))
            {
                SessionTokenPayload? payload = _Protector.Unprotect(bearer);
                if (payload == null) throw Invalid("invalid_token", "The bearer token is not valid. Sign in again.");
                if (payload.Exp <= now) throw Invalid("session_expired", "Your session expired. Sign in again.");
                principal = await SessionStore.SessionAsync(_Database, payload.Sid, token).ConfigureAwait(false);
                if (principal == null || !SameHash(principal.TokenHash, hash) || principal.User.Id != payload.Usr || principal.Tenant.Id != payload.Ten) throw Invalid("invalid_token", "The bearer token is not valid. Sign in again.");
                if (!principal.Active) throw Invalid("session_revoked", "This session was signed out. Sign in again.");
                if (principal.ExpiresUtc == null || principal.ExpiresUtc <= now) throw Invalid("session_expired", "Your session expired. Sign in again.");
                if (!principal.CredentialActive) throw Invalid("credential_inactive", "The credential behind this session is disabled.");
            }
            else
            {
                if (bearer.Length == 0 || bearer.Length > 512) throw Invalid("invalid_token", "The bearer token is not valid.");
                principal = await SessionStore.CredentialAsync(_Database, hash, token).ConfigureAwait(false);
                if (principal == null || !SameHash(principal.TokenHash, hash)) throw Invalid("invalid_token", "The bearer token is not valid.");
                if (!principal.Active) throw Invalid("credential_inactive", "This credential is disabled.");
                if (principal.ExpiresUtc != null && principal.ExpiresUtc <= now) throw Invalid("credential_expired", "This credential expired. Create a new one.");
            }
            if (!principal.Tenant.Active) throw Invalid("tenant_inactive", "This tenant is disabled.");
            if (!principal.User.Active) throw Invalid("user_inactive", "This account is disabled.");
            RequestContext context = new RequestContext
            {
                CorrelationId = received.CorrelationId, ReceivedUtc = received.ReceivedUtc, StartedTimestamp = received.StartedTimestamp, SourceIp = received.SourceIp,
                TenantId = principal.Tenant.Id, UserId = principal.User.Id, IsAuthenticated = true, IsAdmin = principal.User.IsAdmin, IsTenantAdmin = principal.User.IsTenantAdmin || principal.User.IsAdmin,
                Role = principal.User.IsAdmin || principal.User.IsTenantAdmin ? "admin" : "member", AuthScheme = scheme,
                PrincipalType = principal.PrincipalType.Length == 0 ? "User" : principal.PrincipalType, SessionId = principal.PrincipalType == "Credential" && principal.Id == principal.CredentialId ? "" : principal.Id, CredentialId = principal.CredentialId
            };
            if ((principal.LastUsedUtc == null || principal.LastUsedUtc.Value + TouchInterval <= now) && Claim(principal.PrincipalType + ":" + principal.Id, now))
            {
                if (context.SessionId.Length > 0) await SessionStore.TouchSessionAsync(_Database, context.SessionId, token).ConfigureAwait(false);
                else
                {
                    await SessionStore.TouchCredentialAsync(_Database, context.CredentialId, token).ConfigureAwait(false);
                    if (principal.LastUsedUtc == null || principal.LastUsedUtc.Value.Date != now.Date)
                    {
                        AuditEvent used = Event(context, "CredentialUsed", "success", 0, "", "Success", "First use today", scheme, principal.User);
                        used.CredentialId = context.CredentialId; used.PrincipalType = "Credential";
                        await RecordAsync(used, token).ConfigureAwait(false);
                    }
                }
            }
            return context;
        }

        /// <summary>Issue a new token for the caller's session with a fresh lifetime. The previous token stops working.</summary>
        public async Task<TokenResponse> RefreshAsync(RequestContext context, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.SessionId.Length == 0) throw new DomainException(400, "not_refreshable", "Credential tokens do not expire through sessions and cannot be refreshed.");
            StoredPrincipal principal = await SessionStore.SessionAsync(_Database, context.SessionId, token).ConfigureAwait(false) ?? throw Invalid("session_revoked", "This session was signed out. Sign in again.");
            DateTime now = _Clock.GetUtcNow().UtcDateTime; DateTime expires = now + SessionLifetime;
            string tokenId = SecretTokens.Create("", 16);
            string issued = _Protector.Protect(new SessionTokenPayload { Sid = principal.Id, Typ = principal.PrincipalType, Usr = principal.User.Id, Crd = principal.CredentialId, Ten = principal.Tenant.Id, Iat = now, Exp = expires, Jti = tokenId, Sch = context.AuthScheme });
            if (await SessionStore.ReplaceTokenAsync(_Database, principal.Id, principal.TokenHash, tokenId, SecretTokens.Hash(issued), expires, token).ConfigureAwait(false) == 0) throw Invalid("session_revoked", "This session was signed out. Sign in again.");
            AuditEvent refreshed = Event(context, "SessionRefreshed", "success", 200, "", "Success", "", context.AuthScheme, principal.User);
            refreshed.SessionId = principal.Id; refreshed.PrincipalType = principal.PrincipalType;
            await RecordAsync(refreshed, token).ConfigureAwait(false);
            return new TokenResponse { Token = issued, SessionId = principal.Id, ExpiresUtc = expires, User = principal.User, Tenant = principal.Tenant };
        }

        /// <summary>Revoke the caller's session (sign out).</summary>
        public async Task LogoutAsync(RequestContext context, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.SessionId.Length == 0) throw new DomainException(400, "not_a_session", "Credential tokens are revoked by disabling or deleting the credential.");
            await SessionStore.RevokeAsync(_Database, context.SessionId, "Signed out", token).ConfigureAwait(false);
            AuditEvent revoked = Event(context, "SessionRevoked", "success", 200, "", "Success", "Signed out", context.AuthScheme, null);
            revoked.SessionId = context.SessionId; revoked.PrincipalType = context.PrincipalType;
            await RecordAsync(revoked, token).ConfigureAwait(false);
        }

        /// <summary>Describe the caller's token: principal, user, tenant and expiry.</summary>
        public async Task<TokenDetails> DetailsAsync(RequestContext context, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            DatabaseDriverBase.RequireAuthenticated(context);
            User user = await _Database.Identity.UserAsync(context, context.UserId, token).ConfigureAwait(false);
            Tenant tenant = await _Database.Identity.TenantAsync(context, context.TenantId, token).ConfigureAwait(false);
            DateTime? expires = null;
            if (context.SessionId.Length > 0) expires = (await SessionStore.SessionAsync(_Database, context.SessionId, token).ConfigureAwait(false))?.ExpiresUtc;
            else if (context.CredentialId.Length > 0) expires = (await _Database.Identity.CredentialsAsync(context, context.UserId, token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == context.CredentialId)?.ExpiresUtc;
            return new TokenDetails { PrincipalType = context.PrincipalType, AuthScheme = context.AuthScheme, SessionId = context.SessionId, CredentialId = context.CredentialId, ExpiresUtc = expires, User = user, Tenant = tenant };
        }

        /// <summary>Public sign-in page information, including the default credentials.</summary>
        public async Task<LoginInfo> LoginInfoAsync(string version, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(version);
            return new LoginInfo { Version = version, DefaultEmail = IdentitySeed.Email, DefaultPassword = IdentitySeed.Password, DefaultToken = IdentitySeed.Token, MultipleTenants = await SessionStore.ActiveTenantCountAsync(_Database, token).ConfigureAwait(false) > 1 };
        }

        /// <summary>Throw 404 tenant_not_found unless the tenant exists and is active.</summary>
        public async Task RequireTenantAsync(string tenantId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(tenantId);
            RequestContext system = new RequestContext { TenantId = tenantId, UserId = IdentitySeed.UserId, IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, PrincipalType = "Service" };
            Tenant tenant = await _Database.Identity.TenantAsync(system, tenantId, token).ConfigureAwait(false);
            if (!tenant.Active) throw new DomainException(404, "tenant_not_found", "Tenant not found.");
        }

        /// <summary>Persist an audit record.</summary>
        public Task RecordAsync(AuditEvent entry, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return AuditLog.WriteAsync(_Database, entry, token);
        }

        private async Task<TokenResponse> IssueAsync(User user, Tenant tenant, string credentialId, string principalType, string scheme, string sourceIp, string userAgent, CancellationToken token)
        {
            DateTime now = _Clock.GetUtcNow().UtcDateTime; DateTime expires = now + SessionLifetime;
            string id = Ids.New("ses_"); string tokenId = SecretTokens.Create("", 16);
            string issued = _Protector.Protect(new SessionTokenPayload { Sid = id, Typ = principalType, Usr = user.Id, Crd = credentialId, Ten = tenant.Id, Iat = now, Exp = expires, Jti = tokenId, Sch = scheme });
            await SessionStore.IssueAsync(_Database, id, user, credentialId, principalType, scheme, tokenId, SecretTokens.Hash(issued), sourceIp, userAgent, expires, token).ConfigureAwait(false);
            return new TokenResponse { Token = issued, SessionId = id, ExpiresUtc = expires, User = user, Tenant = tenant };
        }

        private static AuditEvent Event(RequestContext context, string operation, string outcome, int status, string error, string authentication, string reason, string scheme, User? user)
        {
            return new AuditEvent { TenantId = user?.TenantId ?? context.TenantId, UserId = user?.Id ?? context.UserId, Operation = operation, Outcome = outcome, StatusCode = status, ErrorCode = error, AuthenticationResult = authentication, Reason = reason, AuthScheme = scheme, CorrelationId = context.CorrelationId, SourceIp = context.SourceIp, PrincipalType = "User" };
        }
        private bool Claim(string key, DateTime now)
        {
            while (true)
            {
                if (_Touched.TryGetValue(key, out DateTime last))
                {
                    if (last + TouchInterval > now) return false;
                    if (_Touched.TryUpdate(key, now, last)) return true;
                }
                else if (_Touched.TryAdd(key, now))
                {
                    if (_Touched.Count > 10000) foreach (KeyValuePair<string, DateTime> stale in _Touched) if (stale.Value + TouchInterval <= now) _Touched.TryRemove(stale.Key, out _);
                    return true;
                }
            }
        }
        private static bool SameHash(string stored, string computed) { return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(computed)); }
        private static DomainException Invalid(string code, string message) { return new DomainException(401, code, message); }
    }
}
