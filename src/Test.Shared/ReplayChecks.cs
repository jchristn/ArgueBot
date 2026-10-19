namespace Test.Shared
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class ReplayChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, RequestContext owner, CancellationToken token)
        {
            await PrincipalBoundaryChecks.RunAsync(database, owner, token).ConfigureAwait(false);
            Conversation conversation = await database.CreateConversationAsync(owner, "Replay retention fixture", token).ConfigureAwait(false);
            try
            {
                for (int index = 0; index < 12; index++) await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = "Replay source " + index }, token: token).ConfigureAwait(false);
                long head = (await database.ReadConversationAsync(owner, conversation.Id, token).ConfigureAwait(false)).LastEventSequence;
                if (head != (await database.ReadEventsAsync(owner, conversation.Id, token: token).ConfigureAwait(false)).Last().Sequence) throw new InvalidDataException("The snapshot bootstrap head does not describe committed replay.");
                await database.PruneEventsAsync(DateTime.UnixEpoch, 3, 1, token).ConfigureAwait(false);
                List<DurableEvent> retained = await database.ReadEventsAsync(owner, conversation.Id, token: token).ConfigureAwait(false);
                if (retained.Count != 3 || retained.Last().Sequence != head || retained[1].Sequence != retained[0].Sequence + 1 || (await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Count != 12) throw new InvalidDataException("Retention changed transcript history, lost sequence order or ignored its count bound.");
                bool gap = false;
                try { await database.ReadEventsAsync(owner, conversation.Id, 1, token: token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "event_history_gap" && exception.StatusCode == 409) { gap = true; }
                if (!gap) throw new InvalidDataException("An expired cursor silently skipped committed history.");
                List<DurableEvent> next = await database.ReadEventsAsync(owner, conversation.Id, retained[0].Sequence - 1, 1, token).ConfigureAwait(false);
                if (next.Single().Sequence != retained[0].Sequence) throw new InvalidDataException("A valid boundary cursor could not resume a bounded replay page.");
                await database.PruneEventsAsync(DateTime.UtcNow.AddMinutes(1), 100, 1, token).ConfigureAwait(false);
                retained = await database.ReadEventsAsync(owner, conversation.Id, token: token).ConfigureAwait(false);
                if (retained.Count != 1 || retained.Single().Sequence != head) throw new InvalidDataException("Age retention removed the final sequence anchor.");
                await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = "After retained anchor" }, token: token).ConfigureAwait(false);
                next = await database.ReadEventsAsync(owner, conversation.Id, head, token: token).ConfigureAwait(false);
                if (next.Single().Sequence != head + 1) throw new InvalidDataException("Pruning reused an old event sequence.");
                bool future = false;
                try { await database.ReadEventsAsync(owner, conversation.Id, head + 2, token: token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "invalid_cursor") { future = true; }
                if (!future) throw new InvalidDataException("A future event cursor was accepted.");
            }
            finally { await database.DeleteConversationAsync(owner, conversation.Id, CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
