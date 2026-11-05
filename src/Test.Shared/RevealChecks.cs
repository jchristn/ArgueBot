namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Revealing bearer tokens and share links on any driver: owners and tenant administrators only, never across tenants, and only what was sealed with the current key.</summary>
    internal static class RevealChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, CancellationToken token)
        {
            TokenSealer? original = database.Sealer;
            try
            {
                RequestContext admin = new RequestContext { TenantId = IdentitySeed.TenantId, UserId = IdentitySeed.UserId, IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true };
                // The default token is recognized by its hash, so it can be revealed even though it predates sealing.
                Require((await database.Identity.RevealCredentialAsync(admin, IdentitySeed.UserId, IdentitySeed.CredentialId, token).ConfigureAwait(false)).Token == IdentitySeed.Token, "The default token must be revealable.");

                // A token created without a sealer cannot be revealed.
                database.Sealer = null;
                AuthCredentialCreated unsealed = await database.Identity.CreateCredentialAsync(admin, IdentitySeed.UserId, new AuthCredentialRequest { Name = "Unsealed fixture" }, token).ConfigureAwait(false);
                await Expect(() => database.Identity.RevealCredentialAsync(admin, IdentitySeed.UserId, unsealed.Credential.Id, token), "not_revealable").ConfigureAwait(false);

                // With a sealer, the owner and a tenant administrator reveal the exact token; members cannot reveal others' tokens; another key cannot.
                database.Sealer = new TokenSealer(RandomNumberGenerator.GetBytes(32));
                User member = await database.Identity.CreateUserAsync(admin, new UserRequest { Email = "reveal-member@example.test", Password = "member-password", FirstName = "Reveal" }, token).ConfigureAwait(false);
                RequestContext memberContext = new RequestContext { TenantId = IdentitySeed.TenantId, UserId = member.Id, IsAuthenticated = true };
                AuthCredentialCreated own = await database.Identity.CreateCredentialAsync(memberContext, member.Id, new AuthCredentialRequest { Name = "Member token" }, token).ConfigureAwait(false);
                Require((await database.Identity.RevealCredentialAsync(memberContext, member.Id, own.Credential.Id, token).ConfigureAwait(false)).Token == own.Token, "An owner must reveal their own token.");
                Require((await database.Identity.RevealCredentialAsync(admin, member.Id, own.Credential.Id, token).ConfigureAwait(false)).Token == own.Token, "A tenant administrator must reveal a member's token.");
                await Expect(() => database.Identity.RevealCredentialAsync(memberContext, IdentitySeed.UserId, IdentitySeed.CredentialId, token), "user_not_found").ConfigureAwait(false);
                RequestContext stranger = new RequestContext { TenantId = "ten_reveal_other", UserId = "usr_reveal_other", IsAuthenticated = true, IsAdmin = false, IsTenantAdmin = true };
                await Expect(() => database.Identity.RevealCredentialAsync(stranger, member.Id, own.Credential.Id, token), "user_not_found").ConfigureAwait(false);
                TokenSealer kept = database.Sealer;
                database.Sealer = new TokenSealer(RandomNumberGenerator.GetBytes(32));
                await Expect(() => database.Identity.RevealCredentialAsync(memberContext, member.Id, own.Credential.Id, token), "not_revealable").ConfigureAwait(false);
                database.Sealer = kept;

                // Share links reveal their exact URL while active, and not after revocation.
                Conversation conversation = await database.CreateConversationAsync(memberContext, "Reveal share fixture", token).ConfigureAwait(false);
                ConversationShareCreated link = await database.Shares.CreateAsync(memberContext, conversation.Id, new ConversationShareRequest { ExpiresInHours = 1 }, "https://arguebot.example.test", token).ConfigureAwait(false);
                ConversationShareCreated again = await database.Shares.RevealAsync(admin, conversation.Id, link.Share.Id, "https://arguebot.example.test", token).ConfigureAwait(false);
                Require(again.Token == link.Token && again.Url == link.Url, "A share link must reveal its exact URL.");
                await database.Shares.RevokeAsync(admin, conversation.Id, link.Share.Id, token).ConfigureAwait(false);
                await Expect(() => database.Shares.RevealAsync(admin, conversation.Id, link.Share.Id, "https://arguebot.example.test", token), "share_inactive").ConfigureAwait(false);

                // Every reveal is audited.
                AuditPage audit = await database.Identity.AuditAsync(admin, new AuditQuery { PageSize = 200 }, token).ConfigureAwait(false);
                Require(audit.Items.Count(item => item.Operation == "CredentialRevealed") >= 3 && audit.Items.Any(item => item.Operation == "ShareRevealed"), "Reveals must be audited.");
                await database.DeleteConversationAsync(admin, conversation.Id, token).ConfigureAwait(false);
                await database.Identity.DeleteUserAsync(admin, member.Id, token).ConfigureAwait(false);
                await database.Identity.DeleteCredentialAsync(admin, IdentitySeed.UserId, unsealed.Credential.Id, token).ConfigureAwait(false);
            }
            finally { database.Sealer = original; }
        }

        private static async Task Expect(Func<Task> action, string code)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) when (ex.Code == code) { return; }
            catch (DomainException ex) { throw new InvalidDataException("Expected " + code + " but got " + ex.Code + ": " + ex.Message); }
            throw new InvalidDataException("Expected " + code + " but the reveal succeeded.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
