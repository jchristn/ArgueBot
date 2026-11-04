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
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Durable run checkpoints and immutable relational participant snapshots.</summary>
    public sealed class RunMethods : IRunMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal RunMethods(DatabaseDriverBase database) { _Database = database; }

        /// <summary>Create one run with immutable snapshots and an idempotency receipt.</summary>
        public async Task<DebateRun> CreateAsync(RequestContext context, string conversationId, CreateRunRequest request, CancellationToken token = default)
        {
            RequireWrite(context);
            Conversation conversation = await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            bool followup = request.Mode == "followup";
            if (request.Mode != "debate" && !followup) throw new DomainException(400, "invalid_mode", "Run mode must be debate or followup.", "mode");
            if (request.ProtocolVersion != 1 && request.ProtocolVersion != 2) throw new DomainException(400, "invalid_protocol", "Use debate protocol 1 or 2.", "protocolVersion");
            if (followup && (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1048576)) throw new DomainException(400, "invalid_question", "Provide a follow-up question up to one MiB.", "question");
            if (!followup && request.Question.Length > 4000) throw new DomainException(400, "invalid_question", "Provide a debate question up to 4000 characters.", "question");
            if (followup) { request.MaxRounds = 1; request.InterventionSeconds = 0; }
            int maximumSeats = request.ProtocolVersion == 2 ? _Database.Debates.MaximumParticipants : 3;
            if (followup && request.ParticipantIds.Count != 1) throw new DomainException(400, "invalid_participants", "Choose exactly one participant to answer a follow-up.", "participantIds");
            if (!followup && request.ParticipantIds.Count < 2) throw new DomainException(400, "too_few_participants", "Choose at least two participants. A single agent can answer a follow-up instead.", "participantIds");
            if (!followup && request.ParticipantIds.Count > maximumSeats) throw new DomainException(400, "participant_limit", string.Format(CultureInfo.InvariantCulture, "Choose at most {0} participants. This service admits up to {0} seats per debate.", maximumSeats), "participantIds");
            if (request.ParticipantIds.Distinct().Count() != request.ParticipantIds.Count) throw new DomainException(400, "duplicate_participant", "Each participant can hold only one seat. Remove the repeated selection.", "participantIds");
            if (request.MaxRounds < 1 || request.MaxRounds > 50) throw new DomainException(400, "invalid_rounds", "Rounds must be 1 through 50.", "maxRounds");
            if (request.InterventionSeconds < 0 || request.InterventionSeconds > 600) throw new DomainException(400, "invalid_countdown", "The moderator countdown must be 0 through 600 seconds.", "interventionSeconds");
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200) throw new DomainException(400, "invalid_key", "A command key up to 200 characters is required.", "idempotencyKey");
            if (request.ProtocolVersion == 2) BudgetMethods.Validate(request.Policy);
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.ProtocolVersion == 1 ? request.Mode + ":" + request.Question + ":" + string.Join(",", request.ParticipantIds) + ":" + request.MaxRounds + ":" + request.InterventionSeconds : JsonSerializer.Serialize(request))));
            string legacyDigest = "";
            if (request.ProtocolVersion == 2 && JsonSerializer.Serialize(request.Policy) == JsonSerializer.Serialize(new DebatePolicy()))
            {
                JsonObject legacy = JsonSerializer.SerializeToNode(request)!.AsObject(); legacy.Remove("Policy");
                legacyDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
            }
            string? existingRun = null;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT digest,result_id FROM command_receipts WHERE tenant_id=@tenant AND user_id=@user AND conversation_id=@conversation AND command_key=@key", P("tenant", context.TenantId, "user", context.UserId, "conversation", conversationId, "key", request.IdempotencyKey)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    if (S(reader, "digest") != digest && S(reader, "digest") != legacyDigest) throw new DomainException(409, "idempotency_conflict", "Command key already has different input.");
                    existingRun = S(reader, "result_id");
                }
            }
            if (existingRun != null) return await ReadAsync(context, conversationId, existingRun, token).ConfigureAwait(false);
            List<RunnerProfile> participants = new List<RunnerProfile>();
            foreach (string id in request.ParticipantIds)
            {
                RunnerProfile profile = await ReadSelectionAsync(context, id, "participantIds", token).ConfigureAwait(false);
                if (!profile.Enabled) throw new DomainException(409, "disabled_profile", "Enable " + profile.Name + " before starting, or choose another participant.", "participantIds");
                if (participants.Any(seat => string.Equals(seat.Name, profile.Name, StringComparison.OrdinalIgnoreCase))) throw new DomainException(409, "duplicate_seat_name", "Two selected participants are named " + profile.Name + ". Rename one so positions and votes stay attributable.", "participantIds");
                profile.Id = Ids.New("par_"); participants.Add(profile);
            }
            RunnerProfile? orchestrator = null;
            if (request.ProtocolVersion == 2 && !followup)
            {
                if (string.IsNullOrWhiteSpace(request.OrchestratorId)) throw new DomainException(400, "missing_orchestrator", "Choose the model profile ArgueBot should use for questions and summaries.", "orchestratorId");
                orchestrator = await ReadSelectionAsync(context, request.OrchestratorId, "orchestratorId", token).ConfigureAwait(false);
                if (!orchestrator.Enabled) throw new DomainException(409, "disabled_profile", "Enable the orchestrator " + orchestrator.Name + " before starting, or choose another.", "orchestratorId");
                orchestrator.Id = Ids.New("par_");
            }
            string runId = Ids.New("run_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                using (DbCommand command = Command(connection, transaction, "SELECT digest,result_id FROM command_receipts WHERE tenant_id=@tenant AND user_id=@user AND conversation_id=@conversation AND command_key=@key", P("tenant", context.TenantId, "user", context.UserId, "conversation", conversationId, "key", request.IdempotencyKey)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (S(reader, "digest") != digest && S(reader, "digest") != legacyDigest) throw new DomainException(409, "idempotency_conflict", "Command key already has different input.");
                        runId = S(reader, "result_id"); return;
                    }
                }
                long busy = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM runs WHERE tenant_id=@tenant AND conversation_id=@id AND state NOT IN ('completed','cancelled','failed')", token, P("tenant", context.TenantId, "id", conversationId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (busy != 0) throw new DomainException(409, "active_run", "A run is already active or paused in this conversation.");
                await ExecuteAsync(connection, transaction, "INSERT INTO runs(id,tenant_id,conversation_id,question,state,max_rounds,intervention_seconds,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@question,'queued',@rounds,@seconds,@now,@now)", token, P("id", runId, "tenant", context.TenantId, "conversation", conversationId, "question", !followup && !string.IsNullOrWhiteSpace(request.Question) ? request.Question : conversation.Title, "rounds", request.MaxRounds, "seconds", request.InterventionSeconds)).ConfigureAwait(false);
                for (int index = 0; index < participants.Count; index++)
                {
                    await ProfileMethods.InsertAsync(connection, transaction, context, participants[index], true, token).ConfigureAwait(false);
                    await ExecuteAsync(connection, transaction, "INSERT INTO run_participants(id,tenant_id,run_id,profile_id,position,created_utc,updated_utc) VALUES(@id,@tenant,@run,@profile,@position,@now,@now)", token, P("id", Ids.New("rpt_"), "tenant", context.TenantId, "run", runId, "profile", participants[index].Id, "position", index)).ConfigureAwait(false);
                }
                if (orchestrator != null)
                {
                    await DebateMethods.CreateAsync(connection, transaction, context, runId, request.Mode, orchestrator, participants, token).ConfigureAwait(false);
                    await BudgetMethods.CreateAsync(connection, transaction, context, runId, request.Policy, token).ConfigureAwait(false);
                }
                await ExecuteAsync(connection, transaction, "INSERT INTO command_receipts(id,tenant_id,user_id,conversation_id,command_key,digest,result_id,created_utc,updated_utc) VALUES(@id,@tenant,@user,@conversation,@key,@digest,@result,@now,@now)", token, P("id", Ids.New("cmd_"), "tenant", context.TenantId, "user", context.UserId, "conversation", conversationId, "key", request.IdempotencyKey, "digest", digest, "result", runId)).ConfigureAwait(false);
                if (followup)
                {
                    string questionId = Ids.New("msg_");
                    await ExecuteAsync(connection, transaction, "INSERT INTO messages(id,tenant_id,conversation_id,run_id,sequence_number,speaker,kind,content,state,created_utc,updated_utc) SELECT @id,@tenant,@conversation,@run,COALESCE(MAX(sequence_number),0)+1,'user','question',@content,'completed',@now,@now FROM messages WHERE conversation_id=@conversation", token, P("id", questionId, "tenant", context.TenantId, "conversation", conversationId, "run", runId, "content", request.Question)).ConfigureAwait(false);
                    await EventAsync(connection, transaction, context.TenantId, conversationId, "message", questionId, "", token).ConfigureAwait(false);
                }
                await ExecuteAsync(connection, transaction, "UPDATE conversations SET updated_utc=@now,revision=revision+1 WHERE tenant_id=@tenant AND id=@conversation", token, P("tenant", context.TenantId, "conversation", conversationId)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, conversationId, "run_created", runId, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
        }

        /// <summary>List runs in an owned conversation.</summary>
        public async Task<List<DebateRun>> EnumerateAsync(RequestContext context, string conversationId, CancellationToken token = default)
        {
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            List<string> ids = new List<string>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT id FROM runs WHERE tenant_id=@tenant AND conversation_id=@conversation ORDER BY created_utc,id", P("tenant", context.TenantId, "conversation", conversationId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) ids.Add(reader.GetString(0)); }
            List<DebateRun> runs = new List<DebateRun>();
            foreach (string id in ids) runs.Add(await ReadAsync(context, conversationId, id, token).ConfigureAwait(false));
            return runs;
        }

        /// <summary>Read a run and its frozen participant configurations.</summary>
        public async Task<DebateRun> ReadAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default)
        {
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                DebateRun run;
                using (DbCommand command = Command(connection, null, "SELECT * FROM runs WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@id", P("tenant", context.TenantId, "conversation", conversationId, "id", runId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Run not found.");
                    run = new DebateRun { Id = runId, ConversationId = conversationId, Question = S(reader, "question"), State = S(reader, "state"), Outcome = S(reader, "outcome"), Round = reader.GetInt32(reader.GetOrdinal("round")), SpeakerIndex = reader.GetInt32(reader.GetOrdinal("speaker_index")), MaxRounds = reader.GetInt32(reader.GetOrdinal("max_rounds")), InterventionSeconds = reader.GetInt32(reader.GetOrdinal("intervention_seconds")), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
                }
                using (DbCommand command = Command(connection, null, "SELECT p.* FROM profiles p JOIN run_participants r ON p.id=r.profile_id AND p.tenant_id=r.tenant_id WHERE r.tenant_id=@tenant AND r.run_id=@run ORDER BY r.position", P("tenant", context.TenantId, "run", runId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) run.Participants.Add(ProfileMethods.Read(reader)); }
                await DebateMethods.LoadAsync(connection, context, run, token).ConfigureAwait(false);
                foreach (RunnerProfile profile in run.Participants) await ProfileMethods.LoadArgumentsAsync(connection, null, profile, token).ConfigureAwait(false);
                return run;
            }
        }

        /// <summary>Recognize an exact committed command retry before checking its now-stale revision.</summary>
        public async Task<bool> IsDuplicateCommandAsync(RequestContext context, string conversationId, string runId, CommandRequest request, CancellationToken token = default)
        {
            RequireWrite(context);
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            ValidateCommandKey(request);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            { return await ReceiptAsync(connection, null, context, conversationId, runId, request, token).ConfigureAwait(false); }
        }
        /// <summary>Atomically persist a moderator checkpoint, durable event and command receipt.</summary>
        public Task SaveCommandAsync(RequestContext context, DebateRun run, CommandRequest request, CancellationToken token = default)
        { ValidateCommandKey(request); return SaveCoreAsync(context, run, request, token); }
        private static void ValidateCommandKey(CommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200) throw new DomainException(400, "invalid_key", "Use an idempotency key between 1 and 200 characters.");
        }
        /// <summary>Read and resolve a selected profile, naming the request field when it is missing or its configuration is unavailable.</summary>
        private async Task<RunnerProfile> ReadSelectionAsync(RequestContext context, string id, string field, CancellationToken token)
        {
            RunnerProfile profile;
            try { profile = await _Database.Profiles.ReadAsync(context, id, token).ConfigureAwait(false); }
            catch (DomainException error) when (error.Code == "not_found") { throw new DomainException(404, "profile_not_found", "A selected profile was deleted. Choose another agent.", field); }
            try { await _Database.Catalog.ResolveAsync(context, profile, token).ConfigureAwait(false); }
            catch (DomainException error) when (error.Field.Length == 0) { throw new DomainException(error.StatusCode, error.Code, profile.Name + ": " + error.Message, field); }
            return profile;
        }
        private static string CommandDigest(string runId, CommandRequest request)
        { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("run-action:" + runId + ":" + JsonSerializer.Serialize(request)))); }
        private static async Task<bool> ReceiptAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, string conversationId, string runId, CommandRequest request, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT digest,result_id FROM command_receipts WHERE tenant_id=@tenant AND user_id=@user AND conversation_id=@conversation AND command_key=@key", P("tenant", context.TenantId, "user", context.UserId, "conversation", conversationId, "key", request.IdempotencyKey)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return false;
                if (S(reader, "digest") != CommandDigest(runId, request) || S(reader, "result_id") != runId) throw new DomainException(409, "idempotency_conflict", "Command key already has different input.");
                return true;
            }
        }
        /// <summary>Persist a checkpoint using the expected revision; emits a durable status event.</summary>
        public Task SaveAsync(RequestContext context, DebateRun run, CancellationToken token = default)
        { return SaveCoreAsync(context, run, null, token); }
        private async Task SaveCoreAsync(RequestContext context, DebateRun run, CommandRequest? request, CancellationToken token)
        {
            RequireWrite(context);
            await _Database.ReadConversationAsync(context, run.ConversationId, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (request != null && await ReceiptAsync(connection, transaction, context, run.ConversationId, run.Id, request, token).ConfigureAwait(false)) return;
                if (request != null && request.Action == "exclude_summary")
                {
                    // Exclude removes every summary from future prompts. Rebuild keeps prior memory active until its replacement validates.
                    await ExecuteAsync(connection, transaction, "UPDATE debate_summaries SET active=0,updated_utc=@now WHERE tenant_id=@tenant AND run_id=@run", token, P("tenant", context.TenantId, "run", run.Id)).ConfigureAwait(false);
                    await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "summary", run.Id, "excluded", token).ConfigureAwait(false);
                }
                if (request != null && request.Action == "edit_proposal") await DebateMethods.EditProposalAsync(connection, transaction, context, run, request.Text, token).ConfigureAwait(false);
                await SaveCheckpointAsync(connection, transaction, context, run, token).ConfigureAwait(false);
                if (request != null) await ExecuteAsync(connection, transaction, "INSERT INTO command_receipts(id,tenant_id,user_id,conversation_id,command_key,digest,result_id,created_utc,updated_utc) VALUES(@id,@tenant,@user,@conversation,@key,@digest,@result,@now,@now)", token, P("id", Ids.New("cmd_"), "tenant", context.TenantId, "user", context.UserId, "conversation", run.ConversationId, "key", request.IdempotencyKey, "digest", CommandDigest(run.Id, request), "result", run.Id)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        internal static async Task SaveCheckpointAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, CancellationToken token)
        {
            int changed = await ExecuteAsync(connection, transaction, "UPDATE runs SET state=@state,outcome=@outcome,round=@round,speaker_index=@speaker,max_rounds=@max,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND conversation_id=@conversation AND revision=@revision", token, P("state", run.State, "outcome", run.Outcome, "round", run.Round, "speaker", run.SpeakerIndex, "max", run.MaxRounds, "tenant", context.TenantId, "id", run.Id, "conversation", run.ConversationId, "revision", run.Revision)).ConfigureAwait(false);
            if (changed == 0) throw new DomainException(409, "revision_conflict", "Run changed. Reload before applying another action.");
            await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "run_status", run.Id, run.State, token).ConfigureAwait(false);
            if (run.ProtocolVersion == 2) await DebateMethods.SaveAsync(connection, transaction, context, run, token).ConfigureAwait(false);
            run.Revision++;
        }
    }
}
