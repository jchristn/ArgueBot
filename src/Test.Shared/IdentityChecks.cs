namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Security;
    using ArgueBot.Core.Services;

    internal static class IdentityChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, ArgueBotPaths paths, CancellationToken token)
        {
            paths.EnsureDirectories();
            RequestContext owner = new RequestContext { IsAuthenticated = true, TenantId = "ten_local", UserId = "usr_local", Role = "admin", IsAdmin = true };
            string email = "identity-" + Guid.NewGuid().ToString("N") + "@example.invalid";
            string password = new string('A', 64); string replacement = new string('B', 64);
            UserRecord user = await database.Users.CreateAsync(owner, new UserRequest { Name = "Identity fixture 議論", Email = email.ToUpperInvariant(), PasswordSha256 = password }, token).ConfigureAwait(false);
            SessionMethods sessions = new SessionMethods(database, new TokenProtector(paths));
            try
            {
                string secret = await sessions.LoginAsync(new LoginRequest { TenantId = owner.TenantId, Email = email, PasswordSha256 = password.ToLowerInvariant() }, token: token).ConfigureAwait(false);
                RequestContext viewer = await sessions.AuthenticateAsync(secret, token).ConfigureAwait(false) ?? throw new InvalidDataException("Password exchange did not resolve its active user.");
                if (viewer.UserId != user.Id || viewer.Role != "viewer" || viewer.IsAdmin || user.Email != email || !user.HasPassword || JsonSerializer.Serialize(user).Contains(password, StringComparison.Ordinal)) throw new InvalidDataException("Identity metadata leaked a password or login elevated its principal.");
                if ((await database.Users.ReadAsync(viewer, user.Id, token).ConfigureAwait(false)).Id != user.Id) throw new InvalidDataException("Self metadata read was denied.");
                await DeniedAsync(async () => { await database.Users.EnumerateAsync(viewer, token: token).ConfigureAwait(false); }, 403, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await sessions.LoginAsync(new LoginRequest { TenantId = "ten_foreign", Email = email, PasswordSha256 = password }, token: token).ConfigureAwait(false); }, 401, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await sessions.LoginAsync(new LoginRequest { TenantId = owner.TenantId, Email = email, PasswordSha256 = replacement }, token: token).ConfigureAwait(false); }, 401, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Users.CreateAsync(owner, new UserRequest { Name = "Duplicate", Email = email }, token).ConfigureAwait(false); }, 409, token).ConfigureAwait(false);
                RequestContext tenantAdmin = new RequestContext { IsAuthenticated = true, TenantId = owner.TenantId, UserId = user.Id, IsTenantAdmin = true };
                await DeniedAsync(async () => { await database.Users.CreateAsync(tenantAdmin, new UserRequest { Name = "Escalation", Email = "escalation@example.invalid", IsAdmin = true }, token).ConfigureAwait(false); }, 403, token).ConfigureAwait(false);
                await DeniedAsync(async () => { await database.Users.UpdateAsync(owner, owner.UserId, new UserRequest { Name = "Protected", Email = "protected@example.invalid", ExpectedRevision = 1 }, token).ConfigureAwait(false); }, 403, token).ConfigureAwait(false);
                user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = email, Role = "operator", ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                if ((await sessions.AuthenticateAsync(secret, token).ConfigureAwait(false))?.Role != "operator") throw new InvalidDataException("A session retained stale role privileges.");
                await DeniedAsync(async () => { await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = email, ExpectedRevision = 1 }, token).ConfigureAwait(false); }, 409, token).ConfigureAwait(false);
                user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = email, PasswordSha256 = replacement, ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                if (await sessions.AuthenticateAsync(secret, token).ConfigureAwait(false) != null) throw new InvalidDataException("Password rotation did not revoke the old session.");
                secret = await sessions.LoginAsync(new LoginRequest { TenantId = owner.TenantId, Email = email, PasswordSha256 = replacement }, token: token).ConfigureAwait(false);
                user = await database.Users.UpdateAsync(owner, user.Id, new UserRequest { Name = user.Name, Email = email, Active = false, ExpectedRevision = user.Revision }, token).ConfigureAwait(false);
                if (await sessions.AuthenticateAsync(secret, token).ConfigureAwait(false) != null) throw new InvalidDataException("Disabled user retained an active session.");
                await DeniedAsync(async () => { await sessions.LoginAsync(new LoginRequest { TenantId = owner.TenantId, Email = email, PasswordSha256 = replacement }, token: token).ConfigureAwait(false); }, 401, token).ConfigureAwait(false);
                UserPage page = await database.Users.EnumerateAsync(owner, email, pageSize: 1, token: token).ConfigureAwait(false);
                if (page.TotalCount != 1 || page.Items.Count != 1 || page.Items[0].Active) throw new InvalidDataException("Identity search lost scoped disabled-account metadata.");
            }
            finally
            {
                UserRecord current = await database.Users.ReadAsync(owner, user.Id, CancellationToken.None).ConfigureAwait(false);
                if (current.Active) await database.Users.UpdateAsync(owner, current.Id, new UserRequest { Name = current.Name, Email = current.Email, Active = false, ExpectedRevision = current.Revision }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        private static async Task DeniedAsync(Func<Task> action, int status, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); bool denied = false;
            try { await action().ConfigureAwait(false); }
            catch (DomainException exception) when (exception.StatusCode == status) { denied = true; }
            if (!denied) throw new InvalidDataException("Identity control did not enforce expected status " + status + ".");
        }
    }
}
