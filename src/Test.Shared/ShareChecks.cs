namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    /// <summary>Public links on any driver: tool turns only on request, private directives never, revocation, tenant disablement and deletion with the conversation.</summary>
    internal static class ShareChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, CancellationToken token)
        {
            RequestContext owner = new RequestContext { TenantId = IdentitySeed.TenantId, UserId = IdentitySeed.UserId, IsAuthenticated = true, IsAdmin = true, IsTenantAdmin = true };
            Conversation conversation = await database.CreateConversationAsync(owner, "Share fixture", token, "Is sharing safe?").ConfigureAwait(false);
            await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Speaker = "user", Kind = "note", Content = "Public moderator note" }, token: token).ConfigureAwait(false);
            await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Speaker = "Agent A", Kind = "tool", Content = "Local tool: read_file" }, token: token).ConfigureAwait(false);
            await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Speaker = "user", Kind = "note", Content = "Private directive for one seat", RecipientId = "seat_fixture" }, token: token).ConfigureAwait(false);
            ConversationShareCreated plain = await database.Shares.CreateAsync(owner, conversation.Id, new ConversationShareRequest { ExpiresInHours = 2 }, "https://arguebot.example.test", token).ConfigureAwait(false);
            ConversationShareCreated tools = await database.Shares.CreateAsync(owner, conversation.Id, new ConversationShareRequest { ExpiresInHours = 2, IncludeToolCalls = true }, "https://arguebot.example.test", token).ConfigureAwait(false);
            Require(plain.Url == "https://arguebot.example.test/share/" + plain.Token && plain.Token.Length >= 40, "A link must carry a long random token.");
            SharedTranscript read = await database.Shares.ReadAsync(plain.Token, token).ConfigureAwait(false);
            Require(read.Title == "Share fixture" && read.OpeningQuestion == "Is sharing safe?" && read.Messages.Any(item => item.Content == "Public moderator note"), "The transcript must include the conversation and its public turns.");
            Require(read.Messages.All(item => item.Kind != "tool") && read.Messages.All(item => !item.Content.Contains("Private directive", StringComparison.Ordinal)), "Tool turns need opting in and private directives are never shared.");
            SharedTranscript withTools = await database.Shares.ReadAsync(tools.Token, token).ConfigureAwait(false);
            Require(withTools.Messages.Any(item => item.Kind == "tool") && withTools.Messages.All(item => !item.Content.Contains("Private directive", StringComparison.Ordinal)), "Opting in shows tool turns but still never private directives.");
            Require((await database.Shares.ListAsync(owner, conversation.Id, token).ConfigureAwait(false)).First(item => item.Id == plain.Share.Id).ViewCount == 1, "Each read must count a view.");
            await database.Shares.RevokeAsync(owner, conversation.Id, plain.Share.Id, token).ConfigureAwait(false);
            await Missing(() => database.Shares.ReadAsync(plain.Token, token)).ConfigureAwait(false);
            await Missing(() => database.Shares.ReadAsync("not-a-real-share-token-at-all", token)).ConfigureAwait(false);
            RequestContext stranger = new RequestContext { TenantId = "ten_missing", UserId = "usr_missing", IsAuthenticated = true };
            try { await database.Shares.ListAsync(stranger, conversation.Id, token).ConfigureAwait(false); throw new InvalidDataException("Another tenant listed links."); }
            catch (DomainException ex) when (ex.StatusCode == 404) { }
            await database.DeleteConversationAsync(owner, conversation.Id, token).ConfigureAwait(false);
            await Missing(() => database.Shares.ReadAsync(tools.Token, token)).ConfigureAwait(false);
        }

        private static async Task Missing(Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) when (ex.StatusCode == 404 && ex.Code == "share_not_found") { return; }
            throw new InvalidDataException("A link that should be gone still opened.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
