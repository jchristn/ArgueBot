namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Durable, idempotent moderator context and application receipts.</summary>
    public sealed class InterjectionMethods : IInterjectionMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal InterjectionMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Persist context and invalidate stale agreement before any cancellation is requested.</summary>
        public async Task<InterjectionReceipt> CreateAsync(RequestContext context, string conversationId, InterjectionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            for (int attempt = 1; ; attempt++)
            {
                try { return await CreateOnceAsync(context, conversationId, request, token).ConfigureAwait(false); }
                catch (DomainException exception) when (exception.Code == "revision_conflict" && attempt < 5)
                {
                    // A service-side run update committed between the run read and this write. Reread and retry so moderator text is never dropped.
                }
            }
        }

        private async Task<InterjectionReceipt> CreateOnceAsync(RequestContext context, string conversationId, InterjectionRequest request, CancellationToken token)
        {
            RequireWrite(context);
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            if (request.DeliveryMode != "interrupt_now" && request.DeliveryMode != "next_turn") throw new DomainException(400, "invalid_delivery_mode", "Choose Interrupt now or Use in next turn for this interjection.");
            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 1048576 || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200) throw new DomainException(400, "invalid_interjection", "Provide context up to one MiB and a command key up to 200 characters.");
            DebateRun? run = request.RunId.Length == 0 ? (await _Database.Runs.EnumerateAsync(context, conversationId, token).ConfigureAwait(false)).FirstOrDefault(candidate => candidate.State != "completed" && candidate.State != "cancelled" && candidate.State != "failed") : await _Database.Runs.ReadAsync(context, conversationId, request.RunId, token).ConfigureAwait(false);
            if (run != null && run.ProtocolVersion != 2) throw new DomainException(409, "legacy_run", "Use legacy notes for this historical run, or start a new model-guided debate.");
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
            InterjectionReceipt receipt = new InterjectionReceipt { Id = Ids.New("inj_"), MessageId = Ids.New("msg_"), RunId = run?.Id ?? request.RunId, DeliveryMode = request.DeliveryMode, Pinned = request.Pinned };
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                using (DbCommand command = Command(connection, transaction, "SELECT * FROM debate_interjections WHERE tenant_id=@tenant AND conversation_id=@conversation AND command_key=@key", P("tenant", context.TenantId, "conversation", conversationId, "key", request.IdempotencyKey)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (S(reader, "digest") != digest) throw new DomainException(409, "idempotency_conflict", "This key already identifies different context or delivery behavior.");
                        receipt = Read(reader); receipt.WasDuplicate = true; return;
                    }
                }
                if (request.RecipientId.Length > 0 && (run == null || !run.Participants.Any(profile => profile.Id == request.RecipientId))) throw new DomainException(400, "invalid_recipient", "Choose an active seat from the target debate.");
                long collision = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM command_receipts WHERE tenant_id=@tenant AND conversation_id=@conversation AND command_key=@key", token, P("tenant", context.TenantId, "conversation", conversationId, "key", request.IdempotencyKey)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (collision != 0) throw new DomainException(409, "idempotency_conflict", "This key already identifies another command.");
                bool active = run != null && run.State != "completed" && run.State != "cancelled" && run.State != "failed";
                // A recipient-only directive must not change state that other seats observe: the shared epoch, round progress or another seat's turn.
                bool directive = request.RecipientId.Length > 0;
                bool recipientSpeaking = directive && active && (run!.Phase == "response" || run.Phase == "ratification") && run.Participants.Count > 0 && DebatePrompts.Profile(run).Id == request.RecipientId;
                receipt.ContextEpoch = run == null ? 0 : run.ContextEpoch + (active && !directive ? 1 : 0);
                receipt.Status = !active ? "post_run" : request.DeliveryMode == "interrupt_now" && (run!.State == "agent_turn" || run.State == "pausing") && (!directive || recipientSpeaking) ? "interrupt_requested" : "queued";
                await ExecuteAsync(connection, transaction, "INSERT INTO messages(id,tenant_id,conversation_id,run_id,sequence_number,speaker,kind,content,recipient_id,state,created_utc,updated_utc) SELECT @id,@tenant,@conversation,@run,COALESCE(MAX(sequence_number),0)+1,'user','interjection',@text,@recipient,'completed',@now,@now FROM messages WHERE tenant_id=@tenant AND conversation_id=@conversation", token, P("id", receipt.MessageId, "tenant", context.TenantId, "conversation", conversationId, "run", receipt.RunId.Length == 0 ? null : receipt.RunId, "text", request.Text, "recipient", request.RecipientId.Length == 0 ? null : request.RecipientId)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO debate_interjections(id,tenant_id,conversation_id,run_id,message_id,delivery_mode,context_epoch,status,pinned,command_key,digest,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@run,@message,@mode,@epoch,@status,@pinned,@key,@digest,@now,@now)", token, P("id", receipt.Id, "tenant", context.TenantId, "conversation", conversationId, "run", receipt.RunId, "message", receipt.MessageId, "mode", request.DeliveryMode, "epoch", receipt.ContextEpoch, "status", receipt.Status, "pinned", request.Pinned ? 1 : 0, "key", request.IdempotencyKey, "digest", digest)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO command_receipts(id,tenant_id,user_id,conversation_id,command_key,digest,result_id,created_utc,updated_utc) VALUES(@id,@tenant,@user,@conversation,@key,@digest,@result,@now,@now)", token, P("id", Ids.New("cmd_"), "tenant", context.TenantId, "user", context.UserId, "conversation", conversationId, "key", request.IdempotencyKey, "digest", digest, "result", receipt.Id)).ConfigureAwait(false);
                if (request.ClarificationId.Length > 0)
                {
                    int changed = await ExecuteAsync(connection, transaction, "UPDATE debate_clarifications SET status=@status,answer_message_id=@message,updated_utc=@now WHERE tenant_id=@tenant AND conversation_id=@conversation AND run_id=@run AND id=@id AND status='open'", token, P("status", request.SkipClarification ? "skipped" : "answered", "message", receipt.MessageId, "tenant", context.TenantId, "conversation", conversationId, "run", receipt.RunId, "id", request.ClarificationId)).ConfigureAwait(false);
                    if (changed != 1) throw new DomainException(409, "clarification_changed", "The clarification is unavailable or was already answered.");
                }
                if (active && !directive)
                {
                    run!.ContextEpoch = receipt.ContextEpoch;
                    run.Phase = "question"; run.ProposalId = ""; run.SpeakerIndex = 0;
                    await RunMethods.SaveCheckpointAsync(connection, transaction, context, run, token).ConfigureAwait(false);
                }
                await ExecuteAsync(connection, transaction, "UPDATE conversations SET revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@conversation", token, P("tenant", context.TenantId, "conversation", conversationId)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, conversationId, "message", receipt.MessageId, "", token).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, conversationId, "interjection", receipt.Id, receipt.Status, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return receipt;
        }
        /// <summary>Read a bounded newest-first page and exact participant deliveries from successful attempts.</summary>
        public async Task<List<InterjectionReceipt>> EnumerateAsync(RequestContext context, string conversationId, int pageNumber = 1, CancellationToken token = default)
        {
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            if (pageNumber < 1 || pageNumber > 100000) throw new DomainException(400, "invalid_page", "Choose a valid page number.");
            List<InterjectionReceipt> result = new List<InterjectionReceipt>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT * FROM debate_interjections WHERE tenant_id=@tenant AND conversation_id=@conversation ORDER BY created_utc DESC,id LIMIT 50 OFFSET @offset", P("tenant", context.TenantId, "conversation", conversationId, "offset", (pageNumber - 1) * 50)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Read(reader)); }
                foreach (InterjectionReceipt receipt in result)
                {
                    using (DbCommand command = Command(connection, null, "SELECT DISTINCT a.participant_id FROM attempt_context c JOIN generation_attempts a ON a.tenant_id=c.tenant_id AND a.id=c.attempt_id JOIN debate_attempts d ON d.attempt_id=a.id AND d.tenant_id=a.tenant_id WHERE c.tenant_id=@tenant AND c.message_id=@message AND (@run='' OR a.run_id=@run) AND a.state='completed' AND d.purpose IN ('response','ratification')", P("tenant", context.TenantId, "message", receipt.MessageId, "run", receipt.RunId)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) receipt.DeliveredParticipantIds.Add(reader.GetString(0)); }
                    using (DbCommand command = Command(connection, null, "SELECT DISTINCT a.participant_id FROM debate_summary_sources x JOIN debate_summaries s ON s.tenant_id=x.tenant_id AND s.id=x.summary_id JOIN attempt_context c ON c.tenant_id=s.tenant_id AND c.message_id=s.message_id JOIN generation_attempts a ON a.tenant_id=c.tenant_id AND a.id=c.attempt_id JOIN debate_attempts d ON d.tenant_id=a.tenant_id AND d.attempt_id=a.id WHERE x.tenant_id=@tenant AND x.message_id=@message AND (@run='' OR a.run_id=@run) AND a.state='completed' AND d.purpose IN ('response','ratification')", P("tenant", context.TenantId, "message", receipt.MessageId, "run", receipt.RunId)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) { string participant = reader.GetString(0); if (!receipt.DeliveredParticipantIds.Contains(participant)) receipt.SummarizedParticipantIds.Add(participant); } }
                    if (receipt.DeliveredParticipantIds.Count > 0) receipt.Status = "applied";
                    else if (receipt.SummarizedParticipantIds.Count > 0) receipt.Status = "summarized";
                }
            }
            return result;
        }
        private static InterjectionReceipt Read(DbDataReader reader)
        { return new InterjectionReceipt { Id = S(reader, "id"), MessageId = S(reader, "message_id"), RunId = S(reader, "run_id"), DeliveryMode = S(reader, "delivery_mode"), ContextEpoch = Convert.ToInt64(reader["context_epoch"], CultureInfo.InvariantCulture), Status = S(reader, "status"), Pinned = Convert.ToInt32(reader["pinned"], CultureInfo.InvariantCulture) != 0 }; }
    }
}
