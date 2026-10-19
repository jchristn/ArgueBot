namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class MessagePageChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, RequestContext owner, CancellationToken token)
        {
            Conversation conversation = await database.CreateConversationAsync(owner, "Paged history fixture", token).ConfigureAwait(false);
            Conversation other = await database.CreateConversationAsync(owner, "Other history fixture", token).ConfigureAwait(false);
            try
            {
                string longText = string.Concat(Enumerable.Repeat("議論🙂 مرحبا ", 1000)) + "\nFull message tail.";
                List<Message> original = new List<Message>();
                for (int index = 0; index < 35; index++) original.Add(await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = index == 0 ? longText : "Message " + index }, token: token).ConfigureAwait(false));
                MessagePage first = await database.ReadMessagePageAsync(owner, conversation.Id, 1, 10, previewCodePoints: 128, token: token).ConfigureAwait(false);
                Message preview = first.Items[0];
                if (first.TotalCount != 35 || first.Items.Count != 10 || first.ThroughSequence != original.Last().Sequence || !preview.ContentTruncated || preview.ContentBytes != Encoding.UTF8.GetByteCount(longText) || preview.Content.Contains("Full message tail.", StringComparison.Ordinal) || char.IsHighSurrogate(preview.Content.Last())) throw new InvalidDataException("History preview lost its Unicode boundary, byte accounting or exact count.");
                Message full = await database.ReadMessageAsync(owner, conversation.Id, preview.Id, token).ConfigureAwait(false);
                if (full.Content != longText || full.ContentTruncated || full.ContentBytes != preview.ContentBytes) throw new InvalidDataException("Explicit full-message loading returned a preview.");
                Message appended = await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = "New after membership capture" }, token: token).ConfigureAwait(false);
                List<string> seen = new List<string>();
                for (int number = 1; number <= 4; number++)
                {
                    MessagePage page = await database.ReadMessagePageAsync(owner, conversation.Id, number, 10, first.ThroughSequence, previewCodePoints: 128, token: token).ConfigureAwait(false);
                    if (page.TotalCount != 35 || page.LatestSequence != appended.Sequence || page.Items.Any(message => message.Sequence > first.ThroughSequence)) throw new InvalidDataException("Concurrent appends changed captured history membership.");
                    seen.AddRange(page.Items.Select(message => message.Id));
                }
                if (!seen.SequenceEqual(original.Select(message => message.Id))) throw new InvalidDataException("History paging duplicated, reordered or lost messages.");
                MessagePage latest = await database.ReadMessagePageAsync(owner, conversation.Id, pageSize: 10, tail: true, token: token).ConfigureAwait(false);
                if (latest.PageNumber != 4 || latest.TotalCount != 36 || latest.Items.Last().Id != appended.Id) throw new InvalidDataException("The latest-page view did not capture new messages.");
                bool denied = false;
                try { await database.ReadMessageAsync(owner, other.Id, preview.Id, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.StatusCode == 404) { denied = true; }
                if (!denied) throw new InvalidDataException("An individual message escaped its conversation scope.");
                denied = false;
                try { await database.ReadMessagePageAsync(new RequestContext { TenantId = "ten_other", UserId = owner.UserId, Role = "admin", IsAuthenticated = true }, conversation.Id, token: token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.StatusCode == 404) { denied = true; }
                if (!denied) throw new InvalidDataException("Message paging crossed a tenant boundary.");
                denied = false;
                try { await database.ReadMessagePageAsync(owner, conversation.Id, throughSequence: appended.Sequence + 1, token: token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "invalid_cursor") { denied = true; }
                if (!denied) throw new InvalidDataException("A future membership cursor was accepted.");
            }
            finally { await database.DeleteConversationAsync(owner, conversation.Id, CancellationToken.None).ConfigureAwait(false); await database.DeleteConversationAsync(owner, other.Id, CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
