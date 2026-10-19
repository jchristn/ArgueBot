namespace Test.Shared
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Threading;
    using System;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;

    internal static class MessageHttpChecks
    {
        internal static async Task RunAsync(ArgueBotClient client, CancellationToken token)
        {
            Conversation conversation = await client.CreateConversationAsync("Bounded HTTP history fixture", token).ConfigureAwait(false);
            string large = string.Concat(Enumerable.Repeat("議論🙂 ", 3000)) + "\nExact complete message tail.";
            for (int index = 0; index < 105; index++) await client.NoteAsync(conversation.Id, index == 0 ? large : "HTTP fixture " + index, token: token).ConfigureAwait(false);
            MessagePage first = await client.MessagePageAsync(conversation.Id, pageSize: 10, token: token).ConfigureAwait(false);
            if (first.Items.Count != 10 || first.TotalCount != 105 || !first.Items[0].ContentTruncated) throw new InvalidDataException("The public history route did not return bounded previews.");
            List<Message> complete = await client.MessagesAsync(conversation.Id, token).ConfigureAwait(false);
            if (complete.Count != 105 || complete[0].Content != large || complete.Any(message => message.ContentTruncated) || complete.Select(message => message.Sequence).Distinct().Count() != 105) throw new InvalidDataException("Paged native enumeration lost full content or duplicated records.");
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(15)); bool expired = false;
                while (!expired)
                {
                    try { await client.SendAsync<List<DurableEvent>>(HttpMethod.Get, "v1.0/api/conversations/" + conversation.Id + "/events?after=1", token: deadline.Token).ConfigureAwait(false); }
                    catch (DomainException exception) when (exception.Code == "event_history_gap" && exception.StatusCode == 409) { expired = true; }
                    if (!expired) await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                }
                if ((await client.MessagesAsync(conversation.Id, token).ConfigureAwait(false)).Count != 105) throw new InvalidDataException("Background replay retention pruned permanent transcript history.");
            }
            bool gap = false;
            try { await foreach (DurableEvent change in client.WatchEventsAsync(conversation.Id, 1, token).ConfigureAwait(false)) { throw new InvalidDataException("An expired SSE cursor returned changes without requiring snapshots."); } }
            catch (DomainException exception) when (exception.StatusCode == 409 && exception.Code == "event_history_gap") { gap = true; }
            if (!gap) throw new InvalidDataException("The SSE preflight did not expose its replay gap.");
            long head = (await client.ConversationAsync(conversation.Id, token).ConfigureAwait(false)).LastEventSequence;
            await using (IAsyncEnumerator<DurableEvent> live = client.WatchEventsAsync(conversation.Id, head, token).GetAsyncEnumerator(token))
            {
                Task<bool> waiting = live.MoveNextAsync().AsTask();
                Message appended = await client.NoteAsync(conversation.Id, "Live after fresh snapshot bootstrap", token: token).ConfigureAwait(false);
                if (!await waiting.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false) || live.Current.Sequence != head + 1 || live.Current.EntityId != appended.Id) throw new InvalidDataException("Fresh-head SSE attachment lost the concurrent committed event.");
            }
            using (CancellationTokenSource cancelled = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                int count = 0; bool stopped = false;
                try { await foreach (Message message in client.EnumerateMessagesAsync(conversation.Id, cancelled.Token).ConfigureAwait(false)) { if (++count == 2) cancelled.Cancel(); } }
                catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { stopped = true; }
                if (!stopped || count != 2) throw new InvalidDataException("Streaming history enumeration ignored caller cancellation.");
            }
        }
    }
}
