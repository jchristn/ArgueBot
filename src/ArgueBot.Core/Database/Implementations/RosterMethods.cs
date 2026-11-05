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

    /// <summary>Versioned seat membership and boundary-applied roster commands.</summary>
    public sealed class RosterMethods : IRosterMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal RosterMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Queue a reviewed roster mutation without rewriting an in-flight participant or historical votes.</summary>
        public async Task QueueAsync(RequestContext context, string conversationId, string runId, RosterChangeRequest request, CancellationToken token = default)
        {
            RequireWrite(context);
            DebateRun run = await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200) throw new DomainException(400, "invalid_key", "A command key up to 200 characters is required.");
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
            // Recognize exact retries before checking the revision or profile's later state.
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                if (await DuplicateAsync(connection, null, context, runId, request.IdempotencyKey, digest, token).ConfigureAwait(false)) return;
            }
            if (run.ProtocolVersion != 2 || run.State == "completed" || run.State == "cancelled" || run.State == "failed") throw new DomainException(409, "invalid_state", "Change the roster of an active model-guided debate.");
            if (request.Revision != run.Revision) throw new DomainException(409, "revision_conflict", "Refresh the debate before changing its roster.");
            if (request.Action != "add" && request.Action != "remove" && request.Action != "replace") throw new DomainException(400, "invalid_roster_action", "Choose add, remove or replace.");
            if (request.Action != "add" && !run.Participants.Any(seat => seat.Id == request.ParticipantId)) throw new DomainException(400, "invalid_participant", "Choose an active participant seat.");
            if (request.Action == "remove" && run.Participants.Count <= 2) throw new DomainException(409, "minimum_roster", "A debate needs two active seats. Replace an agent or stop the debate.");
            if (request.Action == "add" && run.Participants.Count >= _Database.Debates.MaximumParticipants) throw new DomainException(409, "participant_limit", "The panel is at the configured service limit.");
            RunnerProfile? profile = request.Action == "remove" ? null : await SeatSelection.ReadAsync(_Database, context, request.ProfileId, "profileId", token).ConfigureAwait(false);
            if (profile != null)
            {
                if (!profile.Enabled) throw new DomainException(409, "disabled_profile", "Enable the selected profile first.");
                if (run.Participants.Any(seat => seat.Name == profile.Name && seat.Id != request.ParticipantId)) throw new DomainException(409, "duplicate_seat_name", "Use a distinct participant name so positions remain attributable.");
                profile.Id = Ids.New("par_");
            }
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await DuplicateAsync(connection, transaction, context, runId, request.IdempotencyKey, digest, token).ConfigureAwait(false)) return;
                long pending = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM debate_roster_changes WHERE tenant_id=@tenant AND run_id=@run AND status='pending'", token, P("tenant", context.TenantId, "run", runId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (pending != 0) throw new DomainException(409, "roster_change_pending", "Wait for the pending roster change to reach its boundary before submitting another.");
                if (profile != null) await ProfileMethods.InsertAsync(connection, transaction, context, profile, true, token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO debate_roster_changes(id,tenant_id,run_id,action,old_profile_id,new_profile_id,command_key,digest,status,created_utc,updated_utc) VALUES(@id,@tenant,@run,@action,@old,@new,@key,@digest,'pending',@now,@now)", token, P("id", Ids.New("rch_"), "tenant", context.TenantId, "run", runId, "action", request.Action, "old", request.ParticipantId, "new", profile?.Id ?? "", "key", request.IdempotencyKey, "digest", digest)).ConfigureAwait(false);
                await RunMethods.SaveCheckpointAsync(connection, transaction, context, run, token).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, conversationId, "roster_pending", runId, request.Action, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        private static async Task<bool> DuplicateAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, string runId, string key, string digest, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT digest FROM debate_roster_changes WHERE tenant_id=@tenant AND run_id=@run AND command_key=@key", P("tenant", context.TenantId, "run", runId, "key", key)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return false;
                if (reader.GetString(0) != digest) throw new DomainException(409, "idempotency_conflict", "This roster command key already has different input.");
                return true;
            }
        }
        /// <summary>Apply accepted roster changes at a safe execution boundary.</summary>
        public async Task ApplyPendingAsync(RequestContext context, DebateRun run, CancellationToken token)
        {
            if (run.ProtocolVersion != 2 || run.State == "agent_turn" || run.State == "pausing" || run.State == "interrupting" || run.State == "stopping") return;
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                string id; string action; string oldProfile; string newProfile;
                using (DbCommand command = Command(connection, transaction, "SELECT * FROM debate_roster_changes WHERE tenant_id=@tenant AND run_id=@run AND status='pending' ORDER BY created_utc,id LIMIT 1", P("tenant", context.TenantId, "run", run.Id)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) return;
                    id = S(reader, "id"); action = S(reader, "action"); oldProfile = S(reader, "old_profile_id"); newProfile = S(reader, "new_profile_id");
                }
                run.RosterRevision++; run.ContextEpoch++; run.Phase = "question"; run.ProposalId = ""; run.SpeakerIndex = 0;
                if (action != "add") await ExecuteAsync(connection, transaction, "UPDATE debate_members SET active=0,left_revision=@revision,updated_utc=@now WHERE tenant_id=@tenant AND run_id=@run AND profile_id=@profile AND active=1", token, P("revision", run.RosterRevision, "tenant", context.TenantId, "run", run.Id, "profile", oldProfile)).ConfigureAwait(false);
                if (newProfile.Length > 0)
                {
                    int position = Convert.ToInt32(await ScalarAsync(connection, transaction, "SELECT COALESCE(MAX(position),-1)+1 FROM debate_members WHERE tenant_id=@tenant AND run_id=@run", token, P("tenant", context.TenantId, "run", run.Id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                    await ExecuteAsync(connection, transaction, "INSERT INTO debate_members(id,tenant_id,run_id,profile_id,position,joined_revision,created_utc,updated_utc) VALUES(@id,@tenant,@run,@profile,@position,@revision,@now,@now)", token, P("id", Ids.New("seat_"), "tenant", context.TenantId, "run", run.Id, "profile", newProfile, "position", position, "revision", run.RosterRevision)).ConfigureAwait(false);
                }
                await ExecuteAsync(connection, transaction, "UPDATE debate_roster_changes SET status='applied',updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false);
                await RunMethods.SaveCheckpointAsync(connection, transaction, context, run, token).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "roster_changed", run.Id, action, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        /// <summary>Read all seat membership intervals, including removed agents and their names.</summary>
        public async Task<List<DebateSeat>> EnumerateAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default)
        {
            await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            List<DebateSeat> result = new List<DebateSeat>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT m.*,p.name FROM debate_members m JOIN profiles p ON p.tenant_id=m.tenant_id AND p.id=m.profile_id WHERE m.tenant_id=@tenant AND m.run_id=@run ORDER BY m.position,m.id LIMIT 4096", P("tenant", context.TenantId, "run", runId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new DebateSeat { ParticipantId = S(reader, "profile_id"), Name = S(reader, "name"), Position = Convert.ToInt32(reader["position"], CultureInfo.InvariantCulture), Active = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0, JoinedRevision = Convert.ToInt64(reader["joined_revision"], CultureInfo.InvariantCulture), LeftRevision = reader["left_revision"] is DBNull ? null : Convert.ToInt64(reader["left_revision"], CultureInfo.InvariantCulture) });
            }
            return result;
        }
    }
}
