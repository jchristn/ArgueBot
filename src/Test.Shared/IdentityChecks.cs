namespace Test.Shared
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Database.Sqlite;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Default identity seeding, tenant isolation, bypass-flag authorization, credentials and cascading deletes.</summary>
    internal static class IdentityChecks
    {
        internal static async Task SqliteAsync(CancellationToken token)
        {
            string home = Path.Combine(Path.GetTempPath(), "arguebot-identity-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (SqliteDatabaseDriver database = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await database.InitializeAsync(token).ConfigureAwait(false);
                    await AuthenticatorChecks.RunAsync(database, token).ConfigureAwait(false);
                    await ShareChecks.RunAsync(database, token).ConfigureAwait(false);
                    await RunAsync(database, token).ConfigureAwait(false);
                }
                // A restart must keep the changed default password and must not duplicate or reset seeded records.
                using (SqliteDatabaseDriver reopened = new SqliteDatabaseDriver(new ArgueBotPaths(home)))
                {
                    await reopened.InitializeAsync(token).ConfigureAwait(false);
                    RequestContext system = System();
                    Tenant tenant = await reopened.Identity.TenantAsync(system, "ten_local", token).ConfigureAwait(false);
                    User admin = await reopened.Identity.UserAsync(system, "usr_local", token).ConfigureAwait(false);
                    Require(tenant.Name == "Renamed default" && admin.Email == "admin@arguebot", "A restart must not reset a renamed tenant or the default email.");
                    await Reject(() => reopened.Identity.SetPasswordAsync(Self(admin), admin.Id, new PasswordChangeRequest { CurrentPassword = "password", NewPassword = "another-password" }, token), "invalid_password", "currentPassword", "A restart must not restore the default password after it was changed.");
                    Require((await reopened.Identity.CredentialsAsync(system, "usr_local", token).ConfigureAwait(false)).Count(item => item.Protected) == 1, "A restart must not duplicate the default token.");
                }
            }
            finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
        }

        internal static async Task RunAsync(DatabaseDriverBase database, CancellationToken token)
        {
            RequestContext system = System();
            Tenant defaultTenant = await database.Identity.TenantAsync(system, "ten_local", token).ConfigureAwait(false);
            Require(defaultTenant.Name == "Default tenant" && defaultTenant.Protected && defaultTenant.Active, "The default tenant must be seeded, named and protected.");
            User admin = await database.Identity.UserAsync(system, "usr_local", token).ConfigureAwait(false);
            Require(admin.Email == "admin@arguebot" && admin.IsAdmin && admin.IsTenantAdmin && admin.Protected && admin.FirstName == "Default", "The default administrator must be seeded.");
            AuthCredential seeded = (await database.Identity.CredentialsAsync(system, "usr_local", token).ConfigureAwait(false)).Single();
            Require(seeded.Protected && seeded.TokenLast4 == "dmin" && seeded.Name == "Default admin token", "The default bearer token must be seeded and protected.");
            Require(!JsonSerializer.Serialize(admin).Contains("password", StringComparison.OrdinalIgnoreCase), "User responses must never include password material.");

            // Protected defaults resist deletion and disablement.
            await Reject(() => database.Identity.DeleteTenantAsync(system, "ten_local", "Default tenant", token), "protected_record", "", "The default tenant must not be deletable.").ConfigureAwait(false);
            await Reject(() => database.Identity.UpdateTenantAsync(system, "ten_local", new TenantRequest { Name = "Default tenant", Active = false, Revision = defaultTenant.Revision }, token), "protected_record", "", "The default tenant must not be disabled.").ConfigureAwait(false);
            await Reject(() => database.Identity.DeleteCredentialAsync(system, "usr_local", seeded.Id, token), "protected_record", "", "The default token must not be deletable.").ConfigureAwait(false);
            await Reject(() => database.Identity.UpdateUserAsync(system, "usr_local", new UserRequest { Email = "admin@arguebot", FirstName = "Default", LastName = "Admin", IsAdmin = true, IsTenantAdmin = true, Active = false, Revision = admin.Revision }, token), "protected_record", "", "The default administrator must not be disabled.").ConfigureAwait(false);

            // Tenants are system-admin only; a second tenant is isolated.
            Tenant other = await database.Identity.CreateTenantAsync(system, new TenantRequest { Name = "Acme" }, token).ConfigureAwait(false);
            RequestContext otherAdmin = new RequestContext { TenantId = other.Id, UserId = "usr_other_admin", IsAuthenticated = true, IsTenantAdmin = true, Role = "member" };
            User acmeUser = await database.Identity.CreateUserAsync(otherAdmin, new UserRequest { Email = "Pat@Acme.example", FirstName = "Pat", Password = "pat-password-1" }, token).ConfigureAwait(false);
            await Reject(() => database.Identity.CreateUserAsync(otherAdmin, new UserRequest { Email = "pat@acme.example", Password = "another-pass" }, token), "duplicate_email", "email", "Emails must be unique within a tenant, ignoring case.").ConfigureAwait(false);
            User sameEmailElsewhere = await database.Identity.CreateUserAsync(system, new UserRequest { Email = "pat@acme.example", Password = "pat-password-2" }, token).ConfigureAwait(false);
            Require(sameEmailElsewhere.TenantId == "ten_local", "The same email may exist in another tenant.");
            await Reject(() => database.Identity.UserAsync(system, acmeUser.Id, token), "user_not_found", "", "Users in another tenant must not be readable.").ConfigureAwait(false);
            Require((await database.Identity.UsersAsync(otherAdmin, token).ConfigureAwait(false)).Select(item => item.Id).SequenceEqual(new[] { acmeUser.Id }), "A tenant's user list must contain only its own users.");
            await Reject(() => database.Identity.TenantsAsync(otherAdmin, token), "forbidden", "", "Tenant administrators must not list tenants.").ConfigureAwait(false);
            await Reject(() => database.Identity.CreateUserAsync(otherAdmin, new UserRequest { Email = "root@acme.example", Password = "root-password", IsAdmin = true }, token), "forbidden", "", "Only system administrators may create system administrators.").ConfigureAwait(false);

            // A member manages only themselves.
            RequestContext member = Self(acmeUser);
            await Reject(() => database.Identity.UsersAsync(member, token), "forbidden", "", "Members must not list users.").ConfigureAwait(false);
            User renamed = await database.Identity.UpdateUserAsync(member, acmeUser.Id, new UserRequest { Email = acmeUser.Email, FirstName = "Patricia", Active = true, Revision = acmeUser.Revision }, token).ConfigureAwait(false);
            Require(renamed.FirstName == "Patricia", "Members may edit their own names.");
            await Reject(() => database.Identity.UpdateUserAsync(member, acmeUser.Id, new UserRequest { Email = acmeUser.Email, IsTenantAdmin = true, Active = true, Revision = renamed.Revision }, token), "forbidden", "", "Members must not grant themselves administration.").ConfigureAwait(false);

            // Credentials: the token is shown once, stored hashed, and listed by its last four characters.
            AuthCredentialCreated created = await database.Identity.CreateCredentialAsync(member, acmeUser.Id, new AuthCredentialRequest { Name = "CI" }, token).ConfigureAwait(false);
            Require(created.Token.StartsWith("abt_", StringComparison.Ordinal) && created.Token.Length >= 40 && created.Credential.TokenLast4 == SecretTokens.Last4(created.Token), "A new credential must return its token once with matching last four characters.");
            List<AuthCredential> listed = await database.Identity.CredentialsAsync(member, acmeUser.Id, token).ConfigureAwait(false);
            Require(listed.Count == 1 && !JsonSerializer.Serialize(listed).Contains(created.Token, StringComparison.Ordinal), "Credential lists must never include the token.");
            await Reject(() => database.Identity.CredentialsAsync(member, sameEmailElsewhere.Id, token), "user_not_found", "", "Members must not reach credentials of users in other tenants.").ConfigureAwait(false);
            await Reject(() => database.Identity.CreateCredentialAsync(member, acmeUser.Id, new AuthCredentialRequest { Name = "Past", ExpiresUtc = DateTime.UtcNow.AddMinutes(-1) }, token), "invalid_expiry", "expiresUtc", "Credentials must not expire in the past.").ConfigureAwait(false);

            // Passwords: own changes need the current password; administrators can reset.
            await Reject(() => database.Identity.SetPasswordAsync(member, acmeUser.Id, new PasswordChangeRequest { CurrentPassword = "wrong-password", NewPassword = "new-password-1" }, token), "invalid_password", "currentPassword", "A wrong current password must be rejected.").ConfigureAwait(false);
            await database.Identity.SetPasswordAsync(member, acmeUser.Id, new PasswordChangeRequest { CurrentPassword = "pat-password-1", NewPassword = "new-password-1" }, token).ConfigureAwait(false);
            await database.Identity.SetPasswordAsync(otherAdmin, acmeUser.Id, new PasswordChangeRequest { NewPassword = "reset-password-1" }, token).ConfigureAwait(false);

            // Deleting a user cascades to credentials; deleting a tenant cascades to everything in it.
            User acmeAdmin = await database.Identity.CreateUserAsync(otherAdmin, new UserRequest { Email = "admin@acme.example", Password = "acme-admin-pass", IsTenantAdmin = true }, token).ConfigureAwait(false);
            RequestContext acmeAdminContext = Self(acmeAdmin);
            Conversation acmeConversation = await database.CreateConversationAsync(member, "Acme planning", token).ConfigureAwait(false);
            await database.Identity.DeleteUserAsync(acmeAdminContext, acmeUser.Id, token).ConfigureAwait(false);
            Require((await database.Identity.UsersAsync(acmeAdminContext, token).ConfigureAwait(false)).Select(item => item.Id).SequenceEqual(new[] { acmeAdmin.Id }), "A deleted user must be gone.");
            Require((await database.ReadConversationAsync(acmeAdminContext, acmeConversation.Id, token).ConfigureAwait(false)).UserId == acmeAdmin.Id, "A deleted user's conversations must stay in the tenant, credited to the administrator.");
            await Reject(() => database.Identity.DeleteUserAsync(acmeAdminContext, acmeAdmin.Id, token), "own_access", "", "Administrators must not delete themselves.").ConfigureAwait(false);
            await Reject(() => database.Identity.DeleteTenantAsync(system, other.Id, "acme", token), "confirmation_mismatch", "confirmName", "Tenant deletion must require the exact name.").ConfigureAwait(false);
            await database.Identity.DeleteTenantAsync(system, other.Id, "Acme", token).ConfigureAwait(false);
            await Reject(() => database.ReadConversationAsync(acmeAdminContext, acmeConversation.Id, token), "", "", "Deleting a tenant must delete its conversations.").ConfigureAwait(false);
            Require(!(await database.Identity.TenantsAsync(system, token).ConfigureAwait(false)).Any(item => item.Id == other.Id), "A deleted tenant must be gone.");

            // Leave state for the restart check.
            defaultTenant = await database.Identity.TenantAsync(system, "ten_local", token).ConfigureAwait(false);
            await database.Identity.UpdateTenantAsync(system, "ten_local", new TenantRequest { Name = "Renamed default", Active = true, Revision = defaultTenant.Revision }, token).ConfigureAwait(false);
            await database.Identity.SetPasswordAsync(Self(admin), admin.Id, new PasswordChangeRequest { CurrentPassword = "password", NewPassword = "changed-password" }, token).ConfigureAwait(false);
        }

        private static RequestContext System() { return new RequestContext { TenantId = "ten_local", UserId = "usr_local", IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true, Role = "admin" }; }
        private static RequestContext Self(User user) { return new RequestContext { TenantId = user.TenantId, UserId = user.Id, IsAuthenticated = true, IsAdmin = user.IsAdmin, IsTenantAdmin = user.IsTenantAdmin, Role = "member" }; }
        private static async Task Reject(Func<Task> action, string code, string field, string message)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException failure)
            {
                Require((code.Length == 0 || failure.Code == code) && (field.Length == 0 || failure.Field == field), message + " Got " + failure.Code + " (" + failure.Field + ").");
                return;
            }
            throw new InvalidDataException(message);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
