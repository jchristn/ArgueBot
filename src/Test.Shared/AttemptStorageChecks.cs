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

    internal static class AttemptStorageChecks
    {
        internal static async Task RunAsync(DatabaseDriverBase database, RequestContext owner, CancellationToken token)
        {
            RunnerProfile first = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Attempt storage A", Executable = "fixture-not-executed" }, token).ConfigureAwait(false);
            RunnerProfile second = await database.Profiles.CreateAsync(owner, new RunnerProfile { Name = "Attempt storage B", Executable = "fixture-not-executed" }, token).ConfigureAwait(false);
            Conversation conversation = await database.CreateConversationAsync(owner, "Frozen question fixture 議論", token).ConfigureAwait(false);
            try
            {
                DebateRun run = await database.Runs.CreateAsync(owner, conversation.Id, new CreateRunRequest { ParticipantIds = new List<string> { first.Id, second.Id }, IdempotencyKey = "fixture-create" }, token).ConfigureAwait(false);
                Message note = await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = new string('x', 2048) }, token: token).ConfigureAwait(false);
                Message directive = await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, RunId = run.Id, Kind = "steer", RecipientId = run.Participants[0].Id, Content = "Private retry instruction" }, token: token).ConfigureAwait(false);
                Message other = await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, RunId = run.Id, Kind = "steer", RecipientId = run.Participants[1].Id, Content = "Other participant only" }, token: token).ConfigureAwait(false);
                bool rejected = false;
                try { await database.Attempts.BeginAsync(owner, run, new[] { note }, "fixture system prompt", 1024, 100, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "context_budget_exceeded") { rejected = true; }
                if (!rejected) throw new InvalidDataException("Oversized context was accepted.");
                rejected = false;
                try { await database.Attempts.BeginAsync(owner, run, new[] { other }, "fixture system prompt", 10000, 100, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "context_changed") { rejected = true; }
                if (!rejected || (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).TotalCount != 0 || (await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Count != 3) throw new InvalidDataException("Rejected private context leaked a checkpoint or answer.");
                Message unicode = await database.AppendMessageAsync(owner, new Message { ConversationId = conversation.Id, Content = "議論🙂مرحبا" }, token: token).ConfigureAwait(false);
                int exactCharacters = note.Content.Length + directive.Content.Length + unicode.Content.Length;
                List<Message> prepared = await database.ReadGenerationContextAsync(owner, conversation.Id, run.Participants[0].Id, exactCharacters, 3, token).ConfigureAwait(false);
                if (!prepared.Select(message => message.Id).SequenceEqual(new[] { note.Id, directive.Id, unicode.Id }) || prepared.Last().Content != unicode.Content) throw new InvalidDataException("Bounded context preparation changed Unicode, chronology or recipient visibility.");
                foreach (bool countLimit in new[] { false, true })
                {
                    rejected = false;
                    try { await database.ReadGenerationContextAsync(owner, conversation.Id, run.Participants[0].Id, countLimit ? exactCharacters : exactCharacters - 1, countLimit ? 2 : 3, token).ConfigureAwait(false); }
                    catch (DomainException exception) when (exception.Code == "context_budget_exceeded") { rejected = true; }
                    if (!rejected) throw new InvalidDataException("Context preparation silently truncated an over-budget character/message selection.");
                }
                string originalUnicode = unicode.Content; unicode.Content += "changed";
                rejected = false;
                try { await database.UpdateMessageAsync(owner, unicode, "changed", token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "message_finalized") { rejected = true; }
                if (!rejected || (await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false)).Single(message => message.Id == unicode.Id).Content != originalUnicode) throw new InvalidDataException("A completed source changed after its context metadata was captured.");
                rejected = false;
                try { await database.Attempts.BeginAsync(owner, run, new[] { note, directive }, "fixture system prompt", 10000, 1, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "context_budget_exceeded") { rejected = true; }
                if (!rejected) throw new InvalidDataException("The context message-count limit was ignored.");
                GenerationWork work = await database.Attempts.BeginAsync(owner, run, new[] { note, directive }, "fixture system prompt", 10000, 100, token).ConfigureAwait(false);
                await database.Attempts.MarkStreamingAsync(owner, work.Attempt, token).ConfigureAwait(false);
                work.Answer.Content = "Partial checkpoint";
                await database.UpdateMessageAsync(owner, work.Answer, work.Answer.Content, token).ConfigureAwait(false);
                DebateRun concurrent = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, token).ConfigureAwait(false);
                concurrent.State = "pausing"; await database.Runs.SaveAsync(owner, concurrent, token).ConfigureAwait(false);
                work.Answer.State = "completed"; run.State = "paused"; run.SpeakerIndex = 1;
                rejected = false;
                try { await database.Attempts.CompleteAsync(owner, run, work.Attempt, work.Answer, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "revision_conflict") { rejected = true; }
                List<Message> messages = await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false);
                if (!rejected || messages.Single(message => message.Id == directive.Id).State != "completed" || messages.Single(message => message.Id == work.Answer.Id).State != "streaming" || (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).Items.Single().State != "streaming") throw new InvalidDataException("A stale completion partially advanced execution or consumed a directive.");
                await database.InitializeAsync(token).ConfigureAwait(false);
                run = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, token).ConfigureAwait(false);
                GenerationAttempt interrupted = (await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, token: token).ConfigureAwait(false)).Items.Single();
                if (run.State != "paused" || run.SpeakerIndex != 0 || interrupted.State != "interrupted" || interrupted.ErrorCode != "startup_uncertain") throw new InvalidDataException("Startup did not preserve explicit recovery and the original speaker.");
                run.State = "queued"; await database.Runs.SaveAsync(owner, run, token).ConfigureAwait(false);
                GenerationWork retry = await database.Attempts.BeginAsync(owner, run, new[] { note, directive }, "fixture system prompt", 10000, 100, token).ConfigureAwait(false);
                await database.Attempts.MarkStreamingAsync(owner, retry.Attempt, token).ConfigureAwait(false);
                retry.Answer.Content = "Completed retry"; await database.UpdateMessageAsync(owner, retry.Answer, retry.Answer.Content, token).ConfigureAwait(false);
                retry.Answer.State = "completed"; run.State = "paused"; run.SpeakerIndex = 1;
                await database.Attempts.CompleteAsync(owner, run, retry.Attempt, retry.Answer, token).ConfigureAwait(false);
                long revision = run.Revision;
                await database.Attempts.CompleteAsync(owner, run, retry.Attempt, retry.Answer, token).ConfigureAwait(false);
                run = await database.Runs.ReadAsync(owner, conversation.Id, run.Id, token).ConfigureAwait(false);
                messages = await database.ReadMessagesAsync(owner, conversation.Id, token).ConfigureAwait(false);
                AttemptPage page = await database.Attempts.EnumerateAsync(owner, conversation.Id, run.Id, 1, 1, token).ConfigureAwait(false);
                if (run.Revision != revision || run.SpeakerIndex != 1 || page.TotalCount != 2 || page.Items.Single().Number != 2 || page.Items.Single().ContextMessageIds.SequenceEqual(new[] { note.Id, directive.Id }) == false || messages.Single(message => message.Id == directive.Id).State != "consumed" || messages.Single(message => message.Id == other.Id).State != "completed") throw new InvalidDataException("Terminal retry advanced twice, lost context provenance or consumed another participant's directive.");
                rejected = false;
                try { await database.Attempts.EnumerateAsync(new RequestContext { IsAuthenticated = true, TenantId = "ten_other", UserId = owner.UserId, Role = "admin" }, conversation.Id, run.Id, token: token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.StatusCode == 404) { rejected = true; }
                if (!rejected) throw new InvalidDataException("Another tenant could inspect attempt provenance.");
                run.State = "cancelled"; await database.Runs.SaveAsync(owner, run, token).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    foreach (DebateRun remaining in await database.Runs.EnumerateAsync(owner, conversation.Id, CancellationToken.None).ConfigureAwait(false))
                    { if (remaining.State != "completed" && remaining.State != "cancelled" && remaining.State != "failed") { remaining.State = "cancelled"; await database.Runs.SaveAsync(owner, remaining, CancellationToken.None).ConfigureAwait(false); } }
                    await database.DeleteConversationAsync(owner, conversation.Id, CancellationToken.None).ConfigureAwait(false);
                }
                finally { await database.Profiles.DeleteAsync(owner, first.Id, CancellationToken.None).ConfigureAwait(false); await database.Profiles.DeleteAsync(owner, second.Id, CancellationToken.None).ConfigureAwait(false); }
            }
        }
    }
}
