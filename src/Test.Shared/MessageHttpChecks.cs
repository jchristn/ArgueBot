namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Client;
    using ArgueBot.Core.Models;

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
