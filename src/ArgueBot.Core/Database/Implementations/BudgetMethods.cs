namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using System;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Transactional budget reservations shared by all runner transports.</summary>
    public sealed class BudgetMethods : IBudgetMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal BudgetMethods(DatabaseDriverBase database) { _Database = database; }
        internal static void Validate(DebatePolicy policy)
        {
            Range(policy.MaxParticipantTurns, 1, 100000, "maxParticipantTurns", "participant turns");
            Range(policy.MaxOrchestrationCalls, 1, 10000, "maxOrchestrationCalls", "orchestration calls");
            Range(policy.MaxCompactionCalls, 1, 10000, "maxCompactionCalls", "compaction calls");
            Range(policy.MaxToolCalls, 1, 100000, "maxToolCalls", "tool calls");
            Range(policy.MaxWallClockSeconds, 1, 604800, "maxWallClockSeconds", "wall-clock seconds");
            Range(policy.MaxContextCharacters, 8192, 16777216, "maxContextCharacters", "context characters");
            if (policy.MaxEstimatedInputTokens < 0) throw new DomainException(400, "invalid_policy", "Estimated input tokens cannot be negative. Use 0 for no estimate limit.", "policy.maxEstimatedInputTokens");
            if (policy.QuestionInstructions.Length > 4000) throw new DomainException(400, "invalid_policy", "Question instructions must fit 4000 characters.", "policy.questionInstructions");
            if (policy.SynthesisInstructions.Length > 4000) throw new DomainException(400, "invalid_policy", "Proposal instructions must fit 4000 characters.", "policy.synthesisInstructions");
            if (policy.ResourceAccess != "off" && policy.ResourceAccess != "read" && policy.ResourceAccess != "read_write") throw new DomainException(400, "invalid_policy", "Resource access is off, read or read_write.", "policy.resourceAccess");
            Range(policy.MaxResourceWritesPerTurn, 0, 100, "maxResourceWritesPerTurn", "resource writes per turn");
            Range(policy.MaxResourceWriteBytesPerTurn, 0, 1048576, "maxResourceWriteBytesPerTurn", "resource bytes written per turn");
            Range(policy.MaxResourcesCreated, 0, 1000, "maxResourcesCreated", "resources created");
            if (policy.ResourceNames.Count > 200) throw new DomainException(400, "invalid_policy", "Name at most 200 visible resources.", "policy.resourceNames");
            policy.ResourceNames = policy.ResourceNames.Select(name => ResourceRules.NormalizeName(name, "policy.resourceNames")).Distinct(StringComparer.Ordinal).ToList();
            policy.ResourceTags = ResourceRules.NormalizeTags(policy.ResourceTags);
            if (policy.SummaryInstructions.Length > 4000) throw new DomainException(400, "invalid_policy", "Summary instructions must fit 4000 characters.", "policy.summaryInstructions");
        }
        private static void Range(long value, long minimum, long maximum, string field, string label)
        {
            if (value < minimum || value > maximum) throw new DomainException(400, "invalid_policy", string.Format(CultureInfo.InvariantCulture, "Use {0:N0} through {1:N0} {2}.", minimum, maximum, label), "policy." + field);
        }
        internal static Task<int> CreateAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string runId, DebatePolicy policy, CancellationToken token)
        { return ExecuteAsync(connection, transaction, "INSERT INTO debate_policies(id,tenant_id,max_participant_turns,max_orchestration_calls,max_compaction_calls,max_tool_calls,max_wall_clock_seconds,max_context_characters,max_estimated_input_tokens,question_instructions,synthesis_instructions,summary_instructions,resource_access,resource_names_json,resource_tags_json,max_resource_writes_per_turn,max_resource_write_bytes_per_turn,max_resources_created,created_utc,updated_utc) VALUES(@id,@tenant,@max_participant_turns,@max_orchestration_calls,@max_compaction_calls,@max_tool_calls,@max_wall_clock_seconds,@max_context_characters,@max_estimated_input_tokens,@question_instructions,@synthesis_instructions,@summary_instructions,@resource_access,@resource_names_json,@resource_tags_json,@max_resource_writes_per_turn,@max_resource_write_bytes_per_turn,@max_resources_created,@now,@now)", token, P("id", runId, "tenant", context.TenantId, "max_participant_turns", policy.MaxParticipantTurns, "max_orchestration_calls", policy.MaxOrchestrationCalls, "max_compaction_calls", policy.MaxCompactionCalls, "max_tool_calls", policy.MaxToolCalls, "max_wall_clock_seconds", policy.MaxWallClockSeconds, "max_context_characters", policy.MaxContextCharacters, "max_estimated_input_tokens", policy.MaxEstimatedInputTokens, "question_instructions", policy.QuestionInstructions, "synthesis_instructions", policy.SynthesisInstructions, "summary_instructions", policy.SummaryInstructions, "resource_access", policy.ResourceAccess, "resource_names_json", JsonSerializer.Serialize(policy.ResourceNames), "resource_tags_json", JsonSerializer.Serialize(policy.ResourceTags), "max_resource_writes_per_turn", policy.MaxResourceWritesPerTurn, "max_resource_write_bytes_per_turn", policy.MaxResourceWriteBytesPerTurn, "max_resources_created", policy.MaxResourcesCreated)); }
        internal static async Task LoadAsync(DbConnection connection, RequestContext context, DebateRun run, CancellationToken token)
        {
            using (DbCommand command = Command(connection, null, "SELECT * FROM debate_policies WHERE tenant_id=@tenant AND id=@id", P("tenant", context.TenantId, "id", run.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return;
                run.Policy.MaxParticipantTurns = Convert.ToInt32(reader["max_participant_turns"], CultureInfo.InvariantCulture);
                run.Policy.MaxOrchestrationCalls = Convert.ToInt32(reader["max_orchestration_calls"], CultureInfo.InvariantCulture);
                run.Policy.MaxCompactionCalls = Convert.ToInt32(reader["max_compaction_calls"], CultureInfo.InvariantCulture);
                run.Policy.MaxToolCalls = Convert.ToInt32(reader["max_tool_calls"], CultureInfo.InvariantCulture);
                run.Policy.MaxWallClockSeconds = Convert.ToInt32(reader["max_wall_clock_seconds"], CultureInfo.InvariantCulture);
                run.Policy.MaxContextCharacters = Convert.ToInt32(reader["max_context_characters"], CultureInfo.InvariantCulture);
                run.Policy.MaxEstimatedInputTokens = Convert.ToInt32(reader["max_estimated_input_tokens"], CultureInfo.InvariantCulture);
                run.Policy.QuestionInstructions = S(reader, "question_instructions");
                run.Policy.SynthesisInstructions = S(reader, "synthesis_instructions");
                run.Policy.SummaryInstructions = S(reader, "summary_instructions");
                run.Policy.ResourceAccess = S(reader, "resource_access");
                run.Policy.ResourceNames = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(S(reader, "resource_names_json")) ?? new System.Collections.Generic.List<string>();
                run.Policy.ResourceTags = JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(S(reader, "resource_tags_json")) ?? new System.Collections.Generic.List<string>();
                run.Policy.MaxResourceWritesPerTurn = Convert.ToInt32(reader["max_resource_writes_per_turn"], CultureInfo.InvariantCulture);
                run.Policy.MaxResourceWriteBytesPerTurn = Convert.ToInt32(reader["max_resource_write_bytes_per_turn"], CultureInfo.InvariantCulture);
                run.Policy.MaxResourcesCreated = Convert.ToInt32(reader["max_resources_created"], CultureInfo.InvariantCulture);
                run.Usage.ResourcesCreated = Convert.ToInt64(reader["resources_created"], CultureInfo.InvariantCulture);
                run.Usage.ParticipantTurns = Convert.ToInt64(reader["participant_turns"], CultureInfo.InvariantCulture);
                run.Usage.OrchestrationCalls = Convert.ToInt64(reader["orchestration_calls"], CultureInfo.InvariantCulture);
                run.Usage.CompactionCalls = Convert.ToInt64(reader["compaction_calls"], CultureInfo.InvariantCulture);
                run.Usage.ToolCalls = Convert.ToInt64(reader["tool_calls"], CultureInfo.InvariantCulture);
                run.Usage.EstimatedInputTokens = Convert.ToInt64(reader["estimated_input_tokens"], CultureInfo.InvariantCulture);
            }
        }
        internal static async Task ReserveAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, GenerationAttempt attempt, CancellationToken token)
        {
            string used = run.Phase == "compaction" ? "compaction_calls" : run.Phase == "question" || run.Phase == "synthesis" ? "orchestration_calls" : "participant_turns";
            int changed = await ExecuteAsync(connection, transaction, "UPDATE debate_policies SET " + used + "=" + used + "+1,estimated_input_tokens=estimated_input_tokens+@estimate,updated_utc=@now WHERE tenant_id=@tenant AND id=@run AND " + used + "<max_" + used + " AND (max_estimated_input_tokens=0 OR estimated_input_tokens+@estimate<=max_estimated_input_tokens)", token, P("tenant", context.TenantId, "run", run.Id, "estimate", attempt.EstimatedInputTokens)).ConfigureAwait(false);
            if (changed != 1) throw new DomainException(409, "debate_budget_exhausted", "This phase or estimated input budget is exhausted. Review usage and start a continuation with new limits; retry does not reset reservations.");
            await ExecuteAsync(connection, transaction, "INSERT INTO debate_attempt_usage(id,tenant_id,estimated_input_tokens,created_utc,updated_utc) VALUES(@id,@tenant,@estimate,@now,@now)", token, P("id", attempt.Id, "tenant", context.TenantId, "estimate", attempt.EstimatedInputTokens)).ConfigureAwait(false);
        }
        /// <summary>Reserve an invocation-scoped tool identity before performing its action. Identical retries do not consume twice.</summary>
        public Task ReserveToolAsync(RequestContext context, string runId, string attemptId, string callId, CancellationToken token = default)
        {
            RequireWrite(context);
            return _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await ScalarAsync(connection, transaction, "SELECT id FROM debate_tool_reservations WHERE tenant_id=@tenant AND attempt_id=@attempt AND call_id=@call", token, P("tenant", context.TenantId, "attempt", attemptId, "call", callId)).ConfigureAwait(false) != null) return;
                int changed = await ExecuteAsync(connection, transaction, "UPDATE debate_policies SET tool_calls=tool_calls+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@run AND tool_calls<max_tool_calls", token, P("tenant", context.TenantId, "run", runId)).ConfigureAwait(false);
                if (changed != 1) throw new DomainException(409, "tool_budget_exhausted", "The run reached its tool-call budget. Start a continuation with reviewed limits.");
                await ExecuteAsync(connection, transaction, "INSERT INTO debate_tool_reservations(id,tenant_id,run_id,attempt_id,call_id,created_utc,updated_utc) VALUES(@id,@tenant,@run,@attempt,@call,@now,@now)", token, P("id", Ids.New("bgt_"), "tenant", context.TenantId, "run", runId, "attempt", attemptId, "call", callId)).ConfigureAwait(false);
            }, token);
        }
    }
}
