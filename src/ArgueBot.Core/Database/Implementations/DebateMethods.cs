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

    /// <summary>Structured multi-agent protocol state. The service serializes execution through its conversation gate.</summary>
    public sealed class DebateMethods : IDebateMethods
    {
        private readonly DatabaseDriverBase _Database;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        /// <summary>Operational admission limit, independent of historical roster sizes.</summary>
        public int MaximumParticipants { get; set; } = 16;
        internal DebateMethods(DatabaseDriverBase database) { _Database = database; }

        internal static async Task CreateAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string runId, string mode, RunnerProfile orchestrator, List<RunnerProfile> seats, CancellationToken token)
        {
            await ProfileMethods.InsertAsync(connection, transaction, context, orchestrator, true, token).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO debate_state(id,run_id,tenant_id,mode,orchestrator_id,created_utc,updated_utc) VALUES(@run,@run,@tenant,@mode,@profile,@now,@now)", token, P("run", runId, "tenant", context.TenantId, "mode", mode, "profile", orchestrator.Id)).ConfigureAwait(false);
            for (int index = 0; index < seats.Count; index++) await InsertSeatAsync(connection, transaction, context, runId, seats[index].Id, index, 1, token).ConfigureAwait(false);
        }

        private static Task<int> InsertSeatAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string runId, string profileId, int position, long revision, CancellationToken token)
        { return ExecuteAsync(connection, transaction, "INSERT INTO debate_members(id,tenant_id,run_id,profile_id,position,joined_revision,created_utc,updated_utc) VALUES(@id,@tenant,@run,@profile,@position,@revision,@now,@now)", token, P("id", Ids.New("seat_"), "tenant", context.TenantId, "run", runId, "profile", profileId, "position", position, "revision", revision)); }

        internal static async Task LoadAsync(DbConnection connection, RequestContext context, DebateRun run, CancellationToken token)
        {
            string orchestratorId;
            using (DbCommand command = Command(connection, null, "SELECT * FROM debate_state WHERE tenant_id=@tenant AND run_id=@run", P("tenant", context.TenantId, "run", run.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return;
                run.ProtocolVersion = 2; run.ExplicitMode = S(reader, "mode"); run.Phase = S(reader, "phase");
                run.ContextEpoch = Convert.ToInt64(reader["context_epoch"], CultureInfo.InvariantCulture); run.RosterRevision = Convert.ToInt64(reader["roster_revision"], CultureInfo.InvariantCulture);
                run.ResumePhase = S(reader, "resume_phase"); run.CompactionRecipientId = S(reader, "compaction_recipient_id"); run.RebuildFromOriginals = Convert.ToInt64(reader["rebuild_from_originals"], CultureInfo.InvariantCulture) != 0; run.CurrentQuestion = S(reader, "question"); run.ProposalId = S(reader, "proposal_id"); orchestratorId = S(reader, "orchestrator_id");
            }
            await BudgetMethods.LoadAsync(connection, context, run, token).ConfigureAwait(false);
            await PromptMethods.LoadRunAsync(connection, context, run, token).ConfigureAwait(false);
            using (DbCommand command = Command(connection, null, "SELECT d.purpose,d.repair,a.error_code FROM debate_attempts d JOIN generation_attempts a ON a.tenant_id=d.tenant_id AND a.id=d.attempt_id WHERE a.tenant_id=@tenant AND a.run_id=@run ORDER BY a.number DESC LIMIT 1", P("tenant", context.TenantId, "run", run.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                // The latest attempt for this step returned invalid structured output: the next attempt asks for a corrected reply until the step retry limit is used.
                if (await reader.ReadAsync(token).ConfigureAwait(false) && (S(reader, "error_code") == "invalid_orchestration_output" || S(reader, "error_code") == "summary_validation_failed") && S(reader, "purpose") == run.Phase)
                {
                    run.RepairNumber = Convert.ToInt32(reader["repair"], CultureInfo.InvariantCulture) + 1;
                    run.RepairPending = run.RepairNumber <= run.Policy.MaxStepRetries;
                }
            }
            run.Participants.Clear();
            using (DbCommand command = Command(connection, null, "SELECT p.* FROM profiles p JOIN debate_members m ON p.tenant_id=m.tenant_id AND p.id=m.profile_id WHERE m.tenant_id=@tenant AND m.run_id=@run AND m.active=1 ORDER BY m.position,m.id", P("tenant", context.TenantId, "run", run.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) run.Participants.Add(ProfileMethods.Read(reader)); }
            using (DbCommand command = Command(connection, null, "SELECT * FROM profiles WHERE tenant_id=@tenant AND id=@id", P("tenant", context.TenantId, "id", orchestratorId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { if (await reader.ReadAsync(token).ConfigureAwait(false)) run.Orchestrator = ProfileMethods.Read(reader); }
            if (run.Orchestrator != null) await ProfileMethods.LoadArgumentsAsync(connection, null, run.Orchestrator, token).ConfigureAwait(false);
            if (run.ProposalId.Length > 0) run.ProposalText = Convert.ToString(await ScalarAsync(connection, null, "SELECT content FROM debate_proposals WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", run.ProposalId)).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
        }

        internal static Task<int> SaveAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, CancellationToken token)
        { return ExecuteAsync(connection, transaction, "UPDATE debate_state SET phase=@phase,context_epoch=@epoch,roster_revision=@roster,question=@question,proposal_id=@proposal,resume_phase=@resume,compaction_recipient_id=@recipient,rebuild_from_originals=@rebuild,updated_utc=@now WHERE tenant_id=@tenant AND run_id=@run", token, P("recipient", run.CompactionRecipientId, "rebuild", run.RebuildFromOriginals ? 1 : 0, "resume", run.ResumePhase, "phase", run.Phase, "epoch", run.ContextEpoch, "roster", run.RosterRevision, "question", run.CurrentQuestion, "proposal", run.ProposalId, "tenant", context.TenantId, "run", run.Id)); }

        internal static async Task BeginAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, GenerationAttempt attempt, CancellationToken token)
        {
            await BudgetMethods.ReserveAsync(connection, transaction, context, run, attempt, token).ConfigureAwait(false);
            attempt.Purpose = run.Phase; attempt.ContextEpoch = run.ContextEpoch; attempt.RosterRevision = run.RosterRevision; attempt.RepairNumber = run.RepairPending ? run.RepairNumber : 0;
            await ExecuteAsync(connection, transaction, "INSERT INTO debate_attempts(id,attempt_id,tenant_id,purpose,context_epoch,roster_revision,proposal_id,repair,created_utc,updated_utc) VALUES(@id,@id,@tenant,@purpose,@epoch,@roster,@proposal,@repair,@now,@now)", token, P("id", attempt.Id, "tenant", context.TenantId, "purpose", run.Phase, "epoch", run.ContextEpoch, "roster", run.RosterRevision, "proposal", run.ProposalId, "repair", attempt.RepairNumber)).ConfigureAwait(false);
            int changed = await ExecuteAsync(connection, transaction, "UPDATE debate_state SET invocations=invocations+1 WHERE tenant_id=@tenant AND run_id=@run AND invocations<100000", token, P("tenant", context.TenantId, "run", run.Id)).ConfigureAwait(false);
            if (changed != 1) throw new DomainException(409, "generation_limit", "The run reached its invocation limit. Start a continuation with reviewed limits.");
        }

        internal static async Task LoadAttemptAsync(DbConnection connection, RequestContext context, GenerationAttempt attempt, CancellationToken token)
        {
            using (DbCommand command = Command(connection, null, "SELECT d.purpose,d.context_epoch,d.roster_revision,d.repair,COALESCE(u.estimated_input_tokens,0) AS estimate FROM debate_attempts d LEFT JOIN debate_attempt_usage u ON u.tenant_id=d.tenant_id AND u.id=d.attempt_id WHERE d.tenant_id=@tenant AND d.attempt_id=@id", P("tenant", context.TenantId, "id", attempt.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return;
                attempt.EstimatedInputTokens = Convert.ToInt64(reader["estimate"], CultureInfo.InvariantCulture);
                attempt.Purpose = S(reader, "purpose"); attempt.ContextEpoch = Convert.ToInt64(reader["context_epoch"], CultureInfo.InvariantCulture); attempt.RosterRevision = Convert.ToInt64(reader["roster_revision"], CultureInfo.InvariantCulture);
                attempt.RepairNumber = Convert.ToInt32(reader["repair"], CultureInfo.InvariantCulture);
            }
        }

        internal static async Task AdvanceAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, GenerationAttempt attempt, Message answer, CancellationToken token)
        {
            if (answer.State != "completed" || run.State == "cancelled") return;
            long pendingRoster = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM debate_roster_changes WHERE tenant_id=@tenant AND run_id=@run AND status='pending'", token, P("tenant", context.TenantId, "run", run.Id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (pendingRoster != 0 || attempt.ContextEpoch != run.ContextEpoch || attempt.RosterRevision != run.RosterRevision)
            { run.Phase = "question"; run.ProposalId = ""; run.SpeakerIndex = 0; return; }
            try
            {
                if (attempt.Purpose == "compaction")
                {
                    await MemoryMethods.CompleteAsync(connection, transaction, context, run, attempt, answer, token).ConfigureAwait(false);
                }
                else if (attempt.Purpose == "question")
                {
                    OrchestrationResponse response = Parse<OrchestrationResponse>(answer.Content);
                    if (string.IsNullOrWhiteSpace(response.Question) || response.Question.Length > 16000) throw InvalidOutput();
                    run.CurrentQuestion = response.Question; run.Phase = "response"; run.SpeakerIndex = 0;
                }
                else if (attempt.Purpose == "response")
                {
                    run.SpeakerIndex++;
                    if (run.SpeakerIndex >= run.Participants.Count) { run.SpeakerIndex = 0; run.Phase = "synthesis"; }
                }
                else if (attempt.Purpose == "synthesis")
                {
                    OrchestrationResponse response = Parse<OrchestrationResponse>(answer.Content);
                    if (string.IsNullOrWhiteSpace(response.Proposal) || response.Proposal.Length > 32000) throw InvalidOutput();
                    run.ProposalId = Ids.New("prp_"); run.ProposalText = response.Proposal;
                    await ExecuteAsync(connection, transaction, "INSERT INTO debate_proposals(id,tenant_id,run_id,message_id,content,digest,context_epoch,roster_revision,created_utc,updated_utc) VALUES(@id,@tenant,@run,@message,@content,@digest,@epoch,@roster,@now,@now)", token, P("id", run.ProposalId, "tenant", context.TenantId, "run", run.Id, "message", answer.Id, "content", response.Proposal, "digest", Digest(response.Proposal), "epoch", run.ContextEpoch, "roster", run.RosterRevision)).ConfigureAwait(false);
                    run.Phase = "ratification"; run.SpeakerIndex = 0;
                    await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "proposal", run.ProposalId, "", token).ConfigureAwait(false);
                }
                else if (attempt.Purpose == "ratification")
                {
                    string votedOn = Convert.ToString(await ScalarAsync(connection, transaction, "SELECT proposal_id FROM debate_attempts WHERE tenant_id=@tenant AND attempt_id=@id", token, P("tenant", context.TenantId, "id", attempt.Id)).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
                    // A moderator edit committed while this vote was generating. The answer stays in the transcript, but it never counts toward the replacement proposal.
                    if (votedOn != run.ProposalId) return;
                    AgreementResponse response = Parse<AgreementResponse>(answer.Content);
                    if (!new[] { "agree", "disagree", "abstain", "needs_information" }.Contains(response.Position) || response.Rationale.Length > 16000) throw InvalidOutput();
                    // The attempt is already bound to its proposal, so the echoed ID only guards against a vote meant for an earlier proposal.
                    // Models often miscopy long random identifiers; an ID that names no other proposal of this run still counts for the open one.
                    if (response.ProposalId != run.ProposalId && response.ProposalId.Length > 0)
                    {
                        long stale = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM debate_proposals WHERE tenant_id=@tenant AND run_id=@run AND id=@id", token, P("tenant", context.TenantId, "run", run.Id, "id", response.ProposalId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        if (stale != 0) throw InvalidOutput();
                    }
                    await RecordVoteAsync(connection, transaction, context, run.ProposalId, attempt.ParticipantId, answer.Id, response.Position, response.Rationale, token).ConfigureAwait(false);
                    int next = await NextVoterAsync(connection, transaction, context, run, token).ConfigureAwait(false);
                    if (next >= 0) run.SpeakerIndex = next;
                    else
                    {
                        long agreed = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM debate_agreements a JOIN debate_members m ON m.tenant_id=a.tenant_id AND m.profile_id=a.profile_id AND m.run_id=@run AND m.active=1 WHERE a.tenant_id=@tenant AND a.proposal_id=@proposal AND a.position='agree' AND a.active=1", token, P("run", run.Id, "tenant", context.TenantId, "proposal", run.ProposalId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        string? pending = agreed == run.Participants.Count || run.Round >= run.MaxRounds ? await PendingDirectiveSeatAsync(connection, transaction, context, run, attempt.Id, token).ConfigureAwait(false) : null;
                        if (pending != null)
                        {
                            // Before finalizing, the recipient of an undelivered private directive votes again with it; other seats' state is unchanged.
                            // The earlier vote stops counting until the recipient votes again; RecordVoteAsync preserves it as history.
                            await ExecuteAsync(connection, transaction, "UPDATE debate_agreements SET active=0,updated_utc=@now WHERE tenant_id=@tenant AND proposal_id=@proposal AND profile_id=@profile", token, P("tenant", context.TenantId, "proposal", run.ProposalId, "profile", pending)).ConfigureAwait(false);
                            run.SpeakerIndex = SpeakerIndexFor(run, pending);
                        }
                        else if (agreed == run.Participants.Count) { run.State = "completed"; run.Outcome = "consensus"; }
                        else if (run.Round >= run.MaxRounds) { run.State = "completed"; run.Outcome = "max_rounds"; }
                        else { run.Round++; run.SpeakerIndex = 0; run.Phase = "question"; run.ProposalId = ""; }
                    }
                    await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "agreement", answer.Id, response.Position, token).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is DomainException || exception is ArgumentException || exception is NullReferenceException)
            {
                answer.State = "failed"; attempt.ErrorCode = exception is DomainException domain ? domain.Code : "invalid_orchestration_output";
                // Bounded repairs: the same step runs again with a correction request until the debate's step retry limit is used. Then, or after any other failure, it pauses for the moderator.
                if ((attempt.ErrorCode == "invalid_orchestration_output" || attempt.ErrorCode == "summary_validation_failed") && attempt.RepairNumber < run.Policy.MaxStepRetries) { run.RepairPending = true; return; }
                run.State = "paused"; run.Outcome = attempt.ErrorCode;
            }
        }

        /// <summary>Replace the open proposal with moderator-edited text. Earlier votes stay as history on the superseded proposal, and every active seat votes again.</summary>
        internal static async Task EditProposalAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, string text, CancellationToken token)
        {
            string messageId = Ids.New("msg_"); string proposalId = Ids.New("prp_");
            await ExecuteAsync(connection, transaction, "INSERT INTO messages(id,tenant_id,conversation_id,run_id,sequence_number,speaker,kind,content,recipient_id,state,created_utc,updated_utc) SELECT @id,@tenant,@conversation,@run,COALESCE(MAX(sequence_number),0)+1,'user','note',@text,NULL,'completed',@now,@now FROM messages WHERE tenant_id=@tenant AND conversation_id=@conversation", token, P("id", messageId, "tenant", context.TenantId, "conversation", run.ConversationId, "run", run.Id, "text", "Moderator revised the proposal:\n\n" + text)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO debate_proposals(id,tenant_id,run_id,message_id,content,digest,context_epoch,roster_revision,created_utc,updated_utc) VALUES(@id,@tenant,@run,@message,@content,@digest,@epoch,@roster,@now,@now)", token, P("id", proposalId, "tenant", context.TenantId, "run", run.Id, "message", messageId, "content", text, "digest", Digest(text), "epoch", run.ContextEpoch, "roster", run.RosterRevision)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE debate_proposals SET active=0,updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", run.ProposalId)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE conversations SET revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@conversation", token, P("tenant", context.TenantId, "conversation", run.ConversationId)).ConfigureAwait(false);
            await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "message", messageId, "", token).ConfigureAwait(false);
            await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "proposal", proposalId, run.ProposalId, token).ConfigureAwait(false);
            run.ProposalId = proposalId; run.ProposalText = text; run.SpeakerIndex = 0; run.RepairPending = false;
        }

        /// <summary>Record a seat's vote. A repeat vote on the same proposal moves the earlier vote into history before replacing it.</summary>
        private static async Task RecordVoteAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string proposalId, string seatId, string messageId, string position, string rationale, CancellationToken token)
        {
            int preserved = await ExecuteAsync(connection, transaction, "INSERT INTO debate_agreement_revisions(id,tenant_id,agreement_id,proposal_id,profile_id,message_id,position,rationale,created_utc,updated_utc) SELECT @revision,tenant_id,id,proposal_id,profile_id,message_id,position,rationale,created_utc,@now FROM debate_agreements WHERE tenant_id=@tenant AND proposal_id=@proposal AND profile_id=@profile", token, P("revision", Ids.New("agv_"), "tenant", context.TenantId, "proposal", proposalId, "profile", seatId)).ConfigureAwait(false);
            if (preserved > 0)
                await ExecuteAsync(connection, transaction, "UPDATE debate_agreements SET message_id=@message,position=@position,rationale=@rationale,active=1,created_utc=@now,updated_utc=@now WHERE tenant_id=@tenant AND proposal_id=@proposal AND profile_id=@profile", token, P("message", messageId, "position", position, "rationale", rationale, "tenant", context.TenantId, "proposal", proposalId, "profile", seatId)).ConfigureAwait(false);
            else
                await ExecuteAsync(connection, transaction, "INSERT INTO debate_agreements(id,tenant_id,proposal_id,profile_id,message_id,position,rationale,created_utc,updated_utc) VALUES(@id,@tenant,@proposal,@profile,@message,@position,@rationale,@now,@now)", token, P("id", Ids.New("agr_"), "tenant", context.TenantId, "proposal", proposalId, "profile", seatId, "message", messageId, "position", position, "rationale", rationale)).ConfigureAwait(false);
        }

        /// <summary>Return the next rotation index whose seat has no current vote on the proposal, or -1 when every active seat has voted.</summary>
        private static async Task<int> NextVoterAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, CancellationToken token)
        {
            for (int index = run.SpeakerIndex + 1; index < run.Participants.Count; index++)
            {
                string seat = run.Participants[(index + run.Round - 1) % run.Participants.Count].Id;
                long votes = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM debate_agreements WHERE tenant_id=@tenant AND proposal_id=@proposal AND profile_id=@profile AND active=1", token, P("tenant", context.TenantId, "proposal", run.ProposalId, "profile", seat)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (votes == 0) return index;
            }
            return -1;
        }

        /// <summary>Find an active seat with a private directive it has not yet received, excluding context delivered by the attempt being completed.</summary>
        private static async Task<string?> PendingDirectiveSeatAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, string attemptId, CancellationToken token)
        {
            object? seat = await ScalarAsync(connection, transaction, "SELECT m.recipient_id FROM messages m JOIN debate_members d ON d.tenant_id=m.tenant_id AND d.profile_id=m.recipient_id AND d.run_id=@run AND d.active=1 WHERE m.tenant_id=@tenant AND m.conversation_id=@conversation AND m.kind='interjection' AND m.state='completed' AND NOT EXISTS(SELECT 1 FROM attempt_context c WHERE c.tenant_id=m.tenant_id AND c.attempt_id=@attempt AND c.message_id=m.id) ORDER BY m.sequence_number LIMIT 1", token, P("run", run.Id, "tenant", context.TenantId, "conversation", run.ConversationId, "attempt", attemptId)).ConfigureAwait(false);
            return seat == null || seat is DBNull ? null : Convert.ToString(seat, CultureInfo.InvariantCulture);
        }

        private static int SpeakerIndexFor(DebateRun run, string seatId)
        {
            int position = run.Participants.FindIndex(seat => seat.Id == seatId);
            if (position < 0) throw new DomainException(409, "invalid_state", "The directive recipient is not an active seat.");
            int count = run.Participants.Count;
            return ((position - (run.Round - 1)) % count + count) % count;
        }

        internal static T Parse<T>(string content) where T : class
        {
            string value = content.Trim();
            if (value.StartsWith("```json\n", StringComparison.Ordinal) && value.EndsWith("```", StringComparison.Ordinal)) value = value.Substring(8, value.Length - 11).Trim();
            try
            {
                return JsonSerializer.Deserialize<T>(value, _Json) ?? throw InvalidOutput();
            }
            catch (JsonException)
            {
                // Some models add a remark after the object. Accept the leading object when the reply starts with one.
                string? leading = LeadingObject(value);
                if (leading == null) throw;
                return JsonSerializer.Deserialize<T>(leading, _Json) ?? throw InvalidOutput();
            }
        }
        private static string? LeadingObject(string value)
        {
            if (!value.StartsWith('{')) return null;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Utf8JsonReader reader = new Utf8JsonReader(bytes);
            try
            {
                if (!reader.Read() || !reader.TrySkip()) return null;
            }
            catch (JsonException)
            {
                return null;
            }
            return Encoding.UTF8.GetString(bytes, 0, (int)reader.BytesConsumed);
        }
        private static DomainException InvalidOutput() { return new DomainException(502, "invalid_orchestration_output", "The model did not return the required structured decision. Inspect the answer and explicitly retry."); }
        private static string Digest(string text) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(); }

        /// <summary>Read bounded proposal history with all recorded positions, including dissent from removed seats.</summary>
        public async Task<List<DebateProposal>> ReadProposalsAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default, int pageNumber = 1)
        {
            CatalogMethods.ValidatePage(pageNumber, 100);
            await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            List<DebateProposal> proposals = new List<DebateProposal>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT p.*,m.speaker FROM debate_proposals p JOIN messages m ON m.tenant_id=p.tenant_id AND m.id=p.message_id WHERE p.tenant_id=@tenant AND p.run_id=@run ORDER BY p.created_utc DESC,p.id LIMIT 50 OFFSET @offset", P("tenant", context.TenantId, "run", runId, "offset", ((long)pageNumber - 1) * 50)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) proposals.Add(new DebateProposal { Id = S(reader, "id"), RunId = runId, MessageId = S(reader, "message_id"), Content = S(reader, "content"), Digest = S(reader, "digest"), ContextEpoch = Convert.ToInt64(reader["context_epoch"], CultureInfo.InvariantCulture), RosterRevision = Convert.ToInt64(reader["roster_revision"], CultureInfo.InvariantCulture), Superseded = Convert.ToInt64(reader["active"], CultureInfo.InvariantCulture) == 0, ModeratorEdited = S(reader, "speaker") == "user" });
                }
                foreach (DebateProposal proposal in proposals.Where(item => item.ModeratorEdited))
                {
                    object? previous = await ScalarAsync(connection, null, "SELECT text FROM events WHERE tenant_id=@tenant AND conversation_id=@conversation AND kind='proposal' AND entity_id=@id ORDER BY sequence_number LIMIT 1", token, P("tenant", context.TenantId, "conversation", conversationId, "id", proposal.Id)).ConfigureAwait(false);
                    proposal.SupersedesId = previous == null || previous is DBNull ? "" : Convert.ToString(previous, CultureInfo.InvariantCulture) ?? "";
                }
                foreach (DebateProposal proposal in proposals)
                {
                    using (DbCommand command = Command(connection, null, "SELECT * FROM debate_agreements WHERE tenant_id=@tenant AND proposal_id=@id ORDER BY created_utc,id", P("tenant", context.TenantId, "id", proposal.Id)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) proposal.Agreements.Add(new DebateAgreement { ParticipantId = S(reader, "profile_id"), MessageId = S(reader, "message_id"), Position = S(reader, "position"), Rationale = S(reader, "rationale"), Superseded = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) == 0 }); }
                    using (DbCommand command = Command(connection, null, "SELECT * FROM debate_agreement_revisions WHERE tenant_id=@tenant AND proposal_id=@id ORDER BY created_utc,id", P("tenant", context.TenantId, "id", proposal.Id)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) proposal.Agreements.Add(new DebateAgreement { ParticipantId = S(reader, "profile_id"), MessageId = S(reader, "message_id"), Position = S(reader, "position"), Rationale = S(reader, "rationale"), Superseded = true }); }
                }
            }
            return proposals;
        }
    }
}
