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
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Durable run checkpoints and immutable relational participant snapshots.</summary>
    public sealed class RunMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal RunMethods(DatabaseDriverBase database) { _Database = database; }

        /// <summary>Create one run with immutable snapshots and an idempotency receipt.</summary>
        public async Task<DebateRun> CreateAsync(RequestContext context, string conversationId, CreateRunRequest request, CancellationToken token = default)
        {
            RequireWrite(context);
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            bool followup = request.Mode == "followup";
            if (request.Mode != "debate" && !followup) throw new DomainException(400, "invalid_mode", "Run mode must be debate or followup.");
            if (followup && (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 1048576)) throw new DomainException(400, "invalid_question", "Provide a follow-up question up to one MiB.");
            if (followup) { request.MaxRounds = 1; request.InterventionSeconds = 0; }
            if ((followup ? request.ParticipantIds.Count != 1 : request.ParticipantIds.Count < 2 || request.ParticipantIds.Count > 3) || request.ParticipantIds.Distinct().Count() != request.ParticipantIds.Count) throw new DomainException(400, "invalid_participants", "Choose two or three distinct participants.");
            if (request.MaxRounds < 1 || request.MaxRounds > 50 || request.InterventionSeconds < 0 || request.InterventionSeconds > 600 || string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw new DomainException(400, "invalid_run", "Rounds must be 1 to 50, countdown 0 to 600 seconds, and an idempotency key is required.");
            List<RunnerProfile> participants = new List<RunnerProfile>();
            foreach (string id in request.ParticipantIds)
            {
                RunnerProfile profile = await _Database.Profiles.ReadAsync(context, id, token).ConfigureAwait(false);
                if (!profile.Enabled) throw new DomainException(409, "disabled_profile", "Enable the selected participant before starting.");
                profile.Id = Ids.New("par_"); participants.Add(profile);
            }
            string runId = Ids.New("run_");
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Mode + ":" + request.Question + ":" + string.Join(",", request.ParticipantIds) + ":" + request.MaxRounds + ":" + request.InterventionSeconds)));
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                using (DbCommand command = Command(connection, transaction, "SELECT digest,result_id FROM command_receipts WHERE tenant_id=@tenant AND user_id=@user AND conversation_id=@conversation AND command_key=@key", P("tenant", context.TenantId, "user", context.UserId, "conversation", conversationId, "key", request.IdempotencyKey)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        if (S(reader, "digest") != digest) throw new DomainException(409, "idempotency_conflict", "Command key already has different input.");
                        runId = S(reader, "result_id"); return;
                    }
                }
                long busy = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM runs WHERE tenant_id=@tenant AND conversation_id=@id AND state NOT IN ('completed','cancelled','failed')", token, P("tenant", context.TenantId, "id", conversationId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (busy != 0) throw new DomainException(409, "active_run", "A run is already active or paused in this conversation.");
                await ExecuteAsync(connection, transaction, "INSERT INTO runs(id,tenant_id,conversation_id,state,max_rounds,intervention_seconds,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,'queued',@rounds,@seconds,@now,@now)", token, P("id", runId, "tenant", context.TenantId, "conversation", conversationId, "rounds", request.MaxRounds, "seconds", request.InterventionSeconds)).ConfigureAwait(false);
                for (int index = 0; index < participants.Count; index++)
                {
                    await ProfileMethods.InsertAsync(connection, transaction, context, participants[index], true, token).ConfigureAwait(false);
                    await ExecuteAsync(connection, transaction, "INSERT INTO run_participants(id,tenant_id,run_id,profile_id,position,created_utc,updated_utc) VALUES(@id,@tenant,@run,@profile,@position,@now,@now)", token, P("id", Ids.New("rpt_"), "tenant", context.TenantId, "run", runId, "profile", participants[index].Id, "position", index)).ConfigureAwait(false);
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
                    run = new DebateRun { Id = runId, ConversationId = conversationId, State = S(reader, "state"), Outcome = S(reader, "outcome"), Round = reader.GetInt32(reader.GetOrdinal("round")), SpeakerIndex = reader.GetInt32(reader.GetOrdinal("speaker_index")), MaxRounds = reader.GetInt32(reader.GetOrdinal("max_rounds")), InterventionSeconds = reader.GetInt32(reader.GetOrdinal("intervention_seconds")), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
                }
                using (DbCommand command = Command(connection, null, "SELECT p.* FROM profiles p JOIN run_participants r ON p.id=r.profile_id AND p.tenant_id=r.tenant_id WHERE r.tenant_id=@tenant AND r.run_id=@run ORDER BY r.position", P("tenant", context.TenantId, "run", runId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) run.Participants.Add(ProfileMethods.Read(reader)); }
                foreach (RunnerProfile profile in run.Participants) await ProfileMethods.LoadArgumentsAsync(connection, null, profile, token).ConfigureAwait(false);
                return run;
            }
        }

        /// <summary>Persist a checkpoint using the expected revision; emits a durable status event.</summary>
        public async Task SaveAsync(RequestContext context, DebateRun run, CancellationToken token = default)
        {
            RequireWrite(context);
            await _Database.ReadConversationAsync(context, run.ConversationId, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                int changed = await ExecuteAsync(connection, transaction, "UPDATE runs SET state=@state,outcome=@outcome,round=@round,speaker_index=@speaker,max_rounds=@max,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND conversation_id=@conversation AND revision=@revision", token, P("state", run.State, "outcome", run.Outcome, "round", run.Round, "speaker", run.SpeakerIndex, "max", run.MaxRounds, "tenant", context.TenantId, "id", run.Id, "conversation", run.ConversationId, "revision", run.Revision)).ConfigureAwait(false);
                if (changed == 0) throw new DomainException(409, "revision_conflict", "Run changed. Reload before applying another action.");
                await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "run_status", run.Id, run.State, token).ConfigureAwait(false);
                run.Revision++;
            }, token).ConfigureAwait(false);
        }
    }
}
