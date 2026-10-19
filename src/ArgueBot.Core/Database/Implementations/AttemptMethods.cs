namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Atomic execution provenance and finalization. External invocation remains deliberately outside database transactions.</summary>
    public sealed class AttemptMethods : IAttemptMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal AttemptMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Accept a bounded exact context and pending answer before external work starts. No directive is consumed until successful completion.</summary>
        public async Task<GenerationWork> BeginAsync(RequestContext context, DebateRun run, IReadOnlyList<Message> selectedContext, string systemPrompt, int maxContextCharacters, int maxContextMessages, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(run); ArgumentNullException.ThrowIfNull(selectedContext); ArgumentNullException.ThrowIfNull(systemPrompt);
            if (maxContextCharacters < 1024 || maxContextCharacters > 16777216 || maxContextMessages < 1 || maxContextMessages > 10000) throw new ArgumentOutOfRangeException(nameof(maxContextCharacters));
            await _Database.ReadConversationAsync(context, run.ConversationId, token).ConfigureAwait(false);
            if ((run.State != "queued" && run.State != "window") || run.SpeakerIndex < 0 || run.SpeakerIndex >= run.Participants.Count) throw new DomainException(409, "invalid_state", "Attempt acceptance requires the next queued/window turn.");
            long characters = systemPrompt.Length;
            foreach (Message source in selectedContext) characters = checked(characters + source.Content.Length);
            if (characters > maxContextCharacters || selectedContext.Count > maxContextMessages) throw new DomainException(409, "context_budget_exceeded", "The selected conversation context exceeds the configured budget. No model was invoked and no transcript text was dropped.");
            if (selectedContext.Select(message => message.Id).Distinct(StringComparer.Ordinal).Count() != selectedContext.Count) throw new DomainException(400, "duplicate_context", "Context messages must be distinct.");
            RunnerProfile profile = run.Participants[run.SpeakerIndex];
            Message answer = new Message { Id = Ids.New("msg_"), ConversationId = run.ConversationId, RunId = run.Id, Speaker = profile.Name, Kind = run.Mode == "followup" ? "final" : run.Round == 1 && run.SpeakerIndex == 0 ? "opening" : "rebuttal", State = "streaming", CreatedUtc = DateTime.UtcNow };
            GenerationAttempt attempt = new GenerationAttempt { Id = Ids.New("att_"), ConversationId = run.ConversationId, RunId = run.Id, MessageId = answer.Id, ParticipantId = profile.Id, Round = run.Round, SpeakerIndex = run.SpeakerIndex, PromptVersion = run.Mode == "followup" ? "followup-v1" : "debate-v1", PromptDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(systemPrompt))).ToLowerInvariant(), ContextCharacters = characters, ContextMessageIds = selectedContext.Select(message => message.Id).ToList() };
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                long pending = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM generation_attempts WHERE tenant_id=@tenant AND run_id=@run AND state IN ('pending','streaming')", token, P("tenant", context.TenantId, "run", run.Id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (pending != 0) throw new DomainException(409, "uncertain_attempt", "An unfinished attempt requires service reconciliation before another external invocation.");
                answer.Sequence = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COALESCE(MAX(sequence_number),0)+1 FROM messages WHERE tenant_id=@tenant AND conversation_id=@conversation", token, P("tenant", context.TenantId, "conversation", run.ConversationId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                attempt.Number = Convert.ToInt32(await ScalarAsync(connection, transaction, "SELECT COALESCE(MAX(number),0)+1 FROM generation_attempts WHERE tenant_id=@tenant AND run_id=@run", token, P("tenant", context.TenantId, "run", run.Id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await ExecuteAsync(connection, transaction, "INSERT INTO messages(id,tenant_id,conversation_id,run_id,sequence_number,speaker,kind,content,state,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@run,@sequence,@speaker,@kind,'','streaming',@now,@now)", token, P("id", answer.Id, "tenant", context.TenantId, "conversation", run.ConversationId, "run", run.Id, "sequence", answer.Sequence, "speaker", answer.Speaker, "kind", answer.Kind)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO generation_attempts(id,tenant_id,conversation_id,run_id,message_id,participant_id,number,round,speaker_index,state,prompt_version,prompt_digest,context_characters,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@run,@message,@participant,@number,@round,@speaker,'pending',@version,@digest,@characters,@now,@now)", token, P("id", attempt.Id, "tenant", context.TenantId, "conversation", run.ConversationId, "run", run.Id, "message", answer.Id, "participant", profile.Id, "number", attempt.Number, "round", run.Round, "speaker", run.SpeakerIndex, "version", attempt.PromptVersion, "digest", attempt.PromptDigest, "characters", characters)).ConfigureAwait(false);
                for (int index = 0; index < selectedContext.Count; index++)
                {
                    Message source = selectedContext[index];
                    int inserted = await ExecuteAsync(connection, transaction, "INSERT INTO attempt_context(id,tenant_id,attempt_id,message_id,position,created_utc,updated_utc) SELECT @id,@tenant,@attempt,id,@position,@now,@now FROM messages WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@message AND state='completed' AND content=@content AND (recipient_id IS NULL OR recipient_id=@participant)", token, P("id", Ids.New("act_"), "tenant", context.TenantId, "attempt", attempt.Id, "position", index, "conversation", run.ConversationId, "message", source.Id, "content", source.Content, "participant", profile.Id)).ConfigureAwait(false);
                    if (inserted != 1) throw new DomainException(409, "context_changed", "A selected context message is unavailable, changed or private to another participant.");
                }
                run.State = "agent_turn";
                await RunMethods.SaveCheckpointAsync(connection, transaction, context, run, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE conversations SET updated_utc=@now,revision=revision+1 WHERE tenant_id=@tenant AND id=@conversation", token, P("tenant", context.TenantId, "conversation", run.ConversationId)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "message", answer.Id, "", token).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "attempt_created", attempt.Id, "pending", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return new GenerationWork { Answer = answer, Attempt = attempt };
        }
        /// <summary>Checkpoint imminent external invocation without claiming a transaction can include the external operation.</summary>
        public async Task MarkStreamingAsync(RequestContext context, GenerationAttempt attempt, CancellationToken token = default)
        {
            RequireWrite(context);
            await _Database.ReadConversationAsync(context, attempt.ConversationId, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                int changed = await ExecuteAsync(connection, transaction, "UPDATE generation_attempts SET state='streaming',updated_utc=@now WHERE tenant_id=@tenant AND conversation_id=@conversation AND run_id=@run AND id=@id AND state='pending'", token, P("tenant", context.TenantId, "conversation", attempt.ConversationId, "run", attempt.RunId, "id", attempt.Id)).ConfigureAwait(false);
                if (changed != 1) throw new DomainException(409, "attempt_state_conflict", "The attempt is no longer pending.");
                await EventAsync(connection, transaction, context.TenantId, attempt.ConversationId, "attempt_status", attempt.Id, "streaming", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            attempt.State = "streaming";
        }
        /// <summary>Finalize all execution checkpoints and successful directive consumption in one transaction. Exact terminal retries never advance a speaker again.</summary>
        public async Task CompleteAsync(RequestContext context, DebateRun run, GenerationAttempt attempt, Message answer, CancellationToken token = default)
        {
            RequireWrite(context);
            if (answer.State != "completed" && answer.State != "interrupted" && answer.State != "failed") throw new ArgumentException("Use a terminal answer state.", nameof(answer));
            if (attempt.RunId != run.Id || attempt.ConversationId != run.ConversationId || attempt.MessageId != answer.Id || answer.RunId != run.Id || answer.ConversationId != run.ConversationId || attempt.DurationMilliseconds < 0 || attempt.ErrorCode.Length > 128) throw new ArgumentException("Attempt and answer must match the checkpoint.");
            await _Database.ReadConversationAsync(context, run.ConversationId, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                using (DbCommand inspect = Command(connection, transaction, "SELECT a.state,a.error_code,m.content,m.state AS message_state FROM generation_attempts a JOIN messages m ON m.tenant_id=a.tenant_id AND m.id=a.message_id WHERE a.tenant_id=@tenant AND a.conversation_id=@conversation AND a.run_id=@run AND a.id=@id AND a.message_id=@message AND a.participant_id=@participant", P("tenant", context.TenantId, "conversation", run.ConversationId, "run", run.Id, "id", attempt.Id, "message", answer.Id, "participant", attempt.ParticipantId)))
                using (DbDataReader reader = await inspect.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Attempt not found.");
                    if (S(reader, "content") != answer.Content) throw new DomainException(409, "stream_revision_conflict", "Flush the exact answer content before finalizing its attempt.");
                    string state = S(reader, "state");
                    if (state != "pending" && state != "streaming")
                    {
                        if (state == answer.State && S(reader, "message_state") == answer.State && S(reader, "error_code") == attempt.ErrorCode) return;
                        throw new DomainException(409, "attempt_state_conflict", "This attempt already has another terminal result.");
                    }
                    if (S(reader, "message_state") != "streaming") throw new DomainException(409, "message_finalized", "This answer was already finalized outside its attempt.");
                }
                await RunMethods.SaveCheckpointAsync(connection, transaction, context, run, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE messages SET state=@state,updated_utc=@now WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@message", token, P("state", answer.State, "tenant", context.TenantId, "conversation", run.ConversationId, "message", answer.Id)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE generation_attempts SET state=@state,error_code=@error,duration_ms=@duration,updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("state", answer.State, "error", attempt.ErrorCode, "duration", attempt.DurationMilliseconds, "tenant", context.TenantId, "id", attempt.Id)).ConfigureAwait(false);
                if (answer.State == "completed")
                {
                    List<string> directives = new List<string>();
                    using (DbCommand command = Command(connection, transaction, "SELECT m.id FROM messages m JOIN attempt_context c ON c.tenant_id=m.tenant_id AND c.message_id=m.id WHERE c.tenant_id=@tenant AND c.attempt_id=@attempt AND m.recipient_id=@participant AND m.state='completed' ORDER BY c.position", P("tenant", context.TenantId, "attempt", attempt.Id, "participant", attempt.ParticipantId)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false)) { while (await reader.ReadAsync(token).ConfigureAwait(false)) directives.Add(reader.GetString(0)); }
                    foreach (string id in directives)
                    {
                        await ExecuteAsync(connection, transaction, "UPDATE messages SET state='consumed',updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false);
                        await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "message_end", id, "", token).ConfigureAwait(false);
                    }
                }
                await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "message_end", answer.Id, "", answer.Content.Length, token).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "attempt_status", attempt.Id, answer.State, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            attempt.State = answer.State;
        }
        /// <summary>Read exact provenance through the same owned-conversation boundary as messages and runs.</summary>
        public async Task<AttemptPage> EnumerateAsync(RequestContext context, string conversationId, string runId, int pageNumber = 1, int pageSize = 25, CancellationToken token = default)
        {
            await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100) throw new DomainException(400, "invalid_page", "Use a positive page and a size from one through one hundred.");
            AttemptPage page = new AttemptPage { PageNumber = pageNumber, PageSize = pageSize };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM generation_attempts WHERE tenant_id=@tenant AND run_id=@run", token, P("tenant", context.TenantId, "run", runId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM generation_attempts WHERE tenant_id=@tenant AND run_id=@run ORDER BY number DESC LIMIT @limit OFFSET @offset", P("tenant", context.TenantId, "run", runId, "limit", pageSize, "offset", checked((long)(pageNumber - 1) * pageSize))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) page.Items.Add(new GenerationAttempt { Id = S(reader, "id"), ConversationId = conversationId, RunId = runId, MessageId = S(reader, "message_id"), ParticipantId = S(reader, "participant_id"), Number = Convert.ToInt32(reader["number"], CultureInfo.InvariantCulture), Round = Convert.ToInt32(reader["round"], CultureInfo.InvariantCulture), SpeakerIndex = Convert.ToInt32(reader["speaker_index"], CultureInfo.InvariantCulture), State = S(reader, "state"), ErrorCode = S(reader, "error_code"), PromptVersion = S(reader, "prompt_version"), PromptDigest = S(reader, "prompt_digest"), ContextCharacters = Convert.ToInt64(reader["context_characters"], CultureInfo.InvariantCulture), DurationMilliseconds = Convert.ToInt64(reader["duration_ms"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) });
                }
                foreach (GenerationAttempt attempt in page.Items)
                {
                    using (DbCommand command = Command(connection, null, "SELECT message_id FROM attempt_context WHERE tenant_id=@tenant AND attempt_id=@id ORDER BY position", P("tenant", context.TenantId, "id", attempt.Id)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false)) { while (await reader.ReadAsync(token).ConfigureAwait(false)) attempt.ContextMessageIds.Add(reader.GetString(0)); }
                }
            }
            return page;
        }
    }
}
