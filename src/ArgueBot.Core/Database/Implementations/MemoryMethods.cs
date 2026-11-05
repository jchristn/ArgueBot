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
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Recipient-filtered prompt selection and source-linked recipient-partitioned context compaction.</summary>
    public sealed class MemoryMethods : IMemoryMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal MemoryMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Select original messages and the latest eligible summary; overflow never silently drops a source.</summary>
        public async Task<List<Message>> SelectAsync(RequestContext context, DebateRun run, RunnerProfile profile, int budget, int limit, bool compaction, CancellationToken token = default)
        {
            await _Database.ReadConversationAsync(context, run.ConversationId, token).ConfigureAwait(false);
            List<Message> result = new List<Message>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                long publicThrough = 0; long privateThrough = 0;
                string recipient = compaction ? run.CompactionRecipientId : profile.Id;
                foreach (string partition in (compaction ? new[] { recipient } : new[] { "", recipient }).Distinct())
                {
                    // A moderator rebuild compacts original evidence instead of extending the summary it is replacing.
                    if (compaction && run.RebuildFromOriginals) continue;
                    using (DbCommand command = Command(connection, null, "SELECT s.message_id,s.through_sequence FROM debate_summaries s JOIN messages m ON m.tenant_id=s.tenant_id AND m.id=s.message_id WHERE s.tenant_id=@tenant AND s.run_id=@run AND s.active=1 AND COALESCE(m.recipient_id,'')=@recipient ORDER BY s.through_sequence DESC,s.created_utc DESC LIMIT 1", P("tenant", context.TenantId, "run", run.Id, "recipient", partition)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            long through = Convert.ToInt64(reader["through_sequence"], CultureInfo.InvariantCulture);
                            if (partition.Length == 0) publicThrough = through; else privateThrough = through;
                            Message summary = await _Database.ReadMessageAsync(context, run.ConversationId, S(reader, "message_id"), token).ConfigureAwait(false);
                            result.Add(summary); budget -= summary.Content.Length;
                        }
                    }
                }
                if (budget < 0) throw Overflow();
                string scope = "m.tenant_id=@tenant AND m.conversation_id=@conversation AND m.state='completed' AND m.kind<>'compaction' AND (m.recipient_id IS NULL OR m.recipient_id=@profile) AND (m.sequence_number>CASE WHEN m.recipient_id IS NULL THEN @publicthrough ELSE @privatethrough END OR EXISTS(SELECT 1 FROM debate_interjections i WHERE i.tenant_id=m.tenant_id AND i.message_id=m.id AND i.pinned=1))";
                if (compaction) scope = "m.tenant_id=@tenant AND m.conversation_id=@conversation AND m.state='completed' AND m.kind<>'compaction' AND COALESCE(m.recipient_id,'')=@profile AND m.sequence_number>CASE WHEN m.recipient_id IS NULL THEN @publicthrough ELSE @privatethrough END";
                List<string> ids = new List<string>();
                using (DbCommand command = Command(connection, null, "SELECT m.id,length(m.content) AS size,m.content FROM messages m WHERE " + scope + " ORDER BY m.sequence_number LIMIT @limit", P("tenant", context.TenantId, "conversation", run.ConversationId, "profile", recipient, "publicthrough", publicThrough, "privatethrough", privateThrough, "limit", limit + 1)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        int size = Convert.ToInt32(reader["size"], CultureInfo.InvariantCulture);
                        if (size <= budget) size = S(reader, "content").Length;
                        if (ids.Count + result.Count >= limit || size > budget)
                        {
                            if (!compaction || ids.Count == 0) throw Overflow();
                            break;
                        }
                        ids.Add(S(reader, "id")); budget -= size;
                    }
                }
                if (compaction && ids.Count == 0) throw Overflow();
                foreach (string id in ids) result.Add(await _Database.ReadMessageAsync(context, run.ConversationId, id, token).ConfigureAwait(false));
            }
            return result;
        }
        /// <summary>Choose only evidence visible to this seat; another seat's private material cannot be selected.</summary>
        public async Task<string> CompactionPartitionAsync(RequestContext context, DebateRun run, string recipientId, CancellationToken token = default)
        {
            await _Database.ReadConversationAsync(context, run.ConversationId, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT COALESCE(m.recipient_id,'') AS recipient,SUM(length(m.content)) AS size FROM messages m WHERE m.tenant_id=@tenant AND m.conversation_id=@conversation AND m.state='completed' AND m.kind<>'compaction' AND (m.recipient_id IS NULL OR m.recipient_id=@recipient) AND m.sequence_number>COALESCE((SELECT MAX(s.through_sequence) FROM debate_summaries s JOIN messages sm ON sm.id=s.message_id AND sm.tenant_id=s.tenant_id WHERE s.tenant_id=@tenant AND s.run_id=@run AND s.active=1 AND COALESCE(sm.recipient_id,'')=COALESCE(m.recipient_id,'')),0) GROUP BY m.recipient_id ORDER BY size DESC", P("tenant", context.TenantId, "conversation", run.ConversationId, "run", run.Id, "recipient", recipientId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false)) return S(reader, "recipient");
                throw Overflow();
            }
        }
        /// <summary>Return which of the supplied message IDs are pinned moderator constraints, in the order given.</summary>
        public async Task<List<string>> PinnedAsync(RequestContext context, string conversationId, IReadOnlyList<string> messageIds, CancellationToken token = default)
        {
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            { return await PinnedAsync(connection, null, context, conversationId, messageIds, token).ConfigureAwait(false); }
        }
        private static async Task<List<string>> PinnedAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, string conversationId, IReadOnlyList<string> messageIds, CancellationToken token)
        {
            HashSet<string> pinned = new HashSet<string>(StringComparer.Ordinal);
            using (DbCommand command = Command(connection, transaction, "SELECT message_id FROM debate_interjections WHERE tenant_id=@tenant AND conversation_id=@conversation AND pinned=1", P("tenant", context.TenantId, "conversation", conversationId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) pinned.Add(reader.GetString(0)); }
            return messageIds.Where(pinned.Contains).ToList();
        }
        internal static async Task CompleteAsync(DbConnection connection, DbTransaction transaction, RequestContext context, DebateRun run, GenerationAttempt attempt, Message answer, CancellationToken token)
        {
            OrchestrationResponse response = DebateMethods.Parse<OrchestrationResponse>(answer.Content);
            string summaryContent = SummaryContent.Validate(response, attempt.PromptVersion.EndsWith("-memory-v2", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(response.Summary) || response.Summary.Length > 16000 || response.SourceIds.Count == 0 || response.SourceIds.Distinct().Count() != response.SourceIds.Count || response.SourceIds.Any(id => !attempt.ContextMessageIds.Contains(id))) throw new DomainException(502, "summary_validation_failed", "Summary must cite supplied sources and fit the output budget.");
            if (attempt.ContextMessageIds.Any(id => !response.SourceIds.Contains(id))) throw new DomainException(502, "summary_validation_failed", "Summary must account for every supplied source.");
            if (response.Sections.Count > 0)
            {
                // Pinned moderator constraints are still delivered verbatim; the memory must also attribute each one so the summary cannot silently drop it.
                string constraints = response.Sections.Single(section => section.Kind == "constraints").Content;
                List<string> unattributed = (await PinnedAsync(connection, transaction, context, run.ConversationId, attempt.ContextMessageIds, token).ConfigureAwait(false)).Where(id => !constraints.Contains(id, StringComparison.Ordinal)).ToList();
                if (unattributed.Count > 0) throw new DomainException(502, "summary_validation_failed", "The moderator constraints section must name the source ID of every pinned constraint it summarizes. Prior memory and original sources are preserved.");
            }
            long sourceCharacters = 0;
            using (DbCommand command = Command(connection, transaction, "SELECT m.content FROM attempt_context c JOIN messages m ON m.tenant_id=c.tenant_id AND m.id=c.message_id WHERE c.tenant_id=@tenant AND c.attempt_id=@attempt", P("tenant", context.TenantId, "attempt", attempt.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) sourceCharacters += reader.GetString(0).Length; }
            if (answer.Content.Length >= sourceCharacters || summaryContent.Length >= sourceCharacters) throw new DomainException(502, "summary_validation_failed", "Compacted memory must be shorter than its supplied evidence, including its structured response and source references. Prior memory and original sources are preserved.");
            string summaryId = Ids.New("sum_");
            long through = 0; List<string> originals = new List<string>();
            foreach (string id in attempt.ContextMessageIds)
            {
                using (DbCommand command = Command(connection, transaction, "SELECT x.message_id FROM debate_summary_sources x JOIN debate_summaries s ON s.tenant_id=x.tenant_id AND s.id=x.summary_id WHERE s.tenant_id=@tenant AND s.message_id=@id ORDER BY x.position", P("tenant", context.TenantId, "id", id)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) originals.Add(reader.GetString(0)); }
                object? sourceSequence = await ScalarAsync(connection, transaction, "SELECT sequence_number FROM messages WHERE tenant_id=@tenant AND id=@id AND COALESCE(recipient_id,'')=@recipient AND kind<>'compaction'", token, P("tenant", context.TenantId, "id", id, "recipient", run.CompactionRecipientId)).ConfigureAwait(false);
                if (sourceSequence != null && sourceSequence is not DBNull) { originals.Add(id); through = Math.Max(through, Convert.ToInt64(sourceSequence, CultureInfo.InvariantCulture)); }
            }
            if (through == 0) throw new DomainException(502, "summary_validation_failed", "Summary made no progress through original evidence.");
            // Resolve every original source before any summary row changes, so a missing source rejects activation without a partial write.
            List<KeyValuePair<string, string>> sourceDigests = new List<KeyValuePair<string, string>>();
            foreach (string id in originals.Distinct())
            {
                object? sourceContent = await ScalarAsync(connection, transaction, "SELECT content FROM messages WHERE tenant_id=@tenant AND id=@id AND COALESCE(recipient_id,'')=@recipient", token, P("tenant", context.TenantId, "id", id, "recipient", run.CompactionRecipientId)).ConfigureAwait(false);
                if (sourceContent == null || sourceContent is DBNull) throw new DomainException(409, "context_changed", "A summary source is unavailable.");
                string content = Convert.ToString(sourceContent, CultureInfo.InvariantCulture) ?? "";
                sourceDigests.Add(new KeyValuePair<string, string>(id, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant()));
            }
            await ExecuteAsync(connection, transaction, "UPDATE debate_summaries SET active=0,updated_utc=@now WHERE tenant_id=@tenant AND run_id=@run AND message_id IN (SELECT id FROM messages WHERE tenant_id=@tenant AND COALESCE(recipient_id,'')=@recipient)", token, P("tenant", context.TenantId, "run", run.Id, "recipient", run.CompactionRecipientId)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO debate_summaries(id,tenant_id,run_id,message_id,attempt_id,content,through_sequence,context_epoch,created_utc,updated_utc) VALUES(@id,@tenant,@run,@message,@attempt,@content,@through,@epoch,@now,@now)", token, P("id", summaryId, "tenant", context.TenantId, "run", run.Id, "message", answer.Id, "attempt", attempt.Id, "content", summaryContent, "through", through, "epoch", run.ContextEpoch)).ConfigureAwait(false);
            for (int sectionIndex = 0; sectionIndex < response.Sections.Count; sectionIndex++)
            {
                SummarySection section = response.Sections[sectionIndex];
                await ExecuteAsync(connection, transaction, "INSERT INTO debate_summary_sections(id,tenant_id,summary_id,kind,content,position,created_utc,updated_utc) VALUES(@id,@tenant,@summary,@kind,@content,@position,@now,@now)", token, P("id", Ids.New("sec_"), "tenant", context.TenantId, "summary", summaryId, "kind", section.Kind, "content", section.Content, "position", sectionIndex)).ConfigureAwait(false);
            }
            int position = 0;
            foreach (KeyValuePair<string, string> source in sourceDigests)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO debate_summary_sources(id,tenant_id,summary_id,message_id,digest,position,created_utc,updated_utc) VALUES(@id,@tenant,@summary,@message,@digest,@position,@now,@now)", token, P("id", Ids.New("src_"), "tenant", context.TenantId, "summary", summaryId, "message", source.Key, "digest", source.Value, "position", position++)).ConfigureAwait(false);
            }
            run.Phase = run.ResumePhase.Length == 0 ? "question" : run.ResumePhase; run.ResumePhase = ""; run.CompactionRecipientId = ""; run.RebuildFromOriginals = false;
            await EventAsync(connection, transaction, context.TenantId, run.ConversationId, "summary", summaryId, "activated", token).ConfigureAwait(false);
        }
        /// <summary>Inspect a bounded summary page, including immutable original source identities.</summary>
        public async Task<List<ContextSummary>> EnumerateAsync(RequestContext context, string conversationId, string runId, int pageNumber = 1, CancellationToken token = default, string id = "", int sourcePageNumber = 1, string? recipientId = null, bool activeOnly = false)
        {
            await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            if (pageNumber < 1 || pageNumber > 100000 || sourcePageNumber < 1 || sourcePageNumber > 10000000) throw new DomainException(400, "invalid_page", "Use a valid page number.");
            List<ContextSummary> result = new List<ContextSummary>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT s.*,COALESCE(m.recipient_id,'') AS recipient FROM debate_summaries s JOIN messages m ON m.tenant_id=s.tenant_id AND m.id=s.message_id WHERE s.tenant_id=@tenant AND s.run_id=@run AND (@id='' OR s.id=@id) AND (@scoped=0 OR m.recipient_id IS NULL OR m.recipient_id=@recipient) AND (@activeonly=0 OR s.active=1) ORDER BY s.created_utc DESC,s.id LIMIT 25 OFFSET @offset", P("tenant", context.TenantId, "run", runId, "id", id, "scoped", recipientId == null ? 0 : 1, "recipient", recipientId ?? "", "activeonly", activeOnly ? 1 : 0, "offset", (pageNumber - 1) * 25)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new ContextSummary { Id = S(reader, "id"), MessageId = S(reader, "message_id"), AttemptId = S(reader, "attempt_id"), RunId = runId, Content = S(reader, "content"), RecipientId = S(reader, "recipient"), SourcePageNumber = sourcePageNumber, ThroughSequence = Convert.ToInt64(reader["through_sequence"], CultureInfo.InvariantCulture), ContextEpoch = Convert.ToInt64(reader["context_epoch"], CultureInfo.InvariantCulture), Active = Convert.ToInt32(reader["active"], CultureInfo.InvariantCulture) != 0 }); }
                foreach (ContextSummary summary in result)
                {
                    summary.SourceCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM debate_summary_sources WHERE tenant_id=@tenant AND summary_id=@id", token, P("tenant", context.TenantId, "id", summary.Id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                    using (DbCommand command = Command(connection, null, "SELECT message_id,digest FROM debate_summary_sources WHERE tenant_id=@tenant AND summary_id=@id ORDER BY position LIMIT 100 OFFSET @offset", P("tenant", context.TenantId, "id", summary.Id, "offset", ((long)sourcePageNumber - 1) * 100)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            summary.SourceIds.Add(reader.GetString(0));
                            summary.Sources.Add(new SummarySource { MessageId = reader.GetString(0), Digest = reader.GetString(1) });
                        }
                    }
                    using (DbCommand command = Command(connection, null, "SELECT s.id FROM attempt_context c JOIN debate_summaries s ON s.tenant_id=c.tenant_id AND s.message_id=c.message_id WHERE c.tenant_id=@tenant AND c.attempt_id=@attempt ORDER BY c.position", P("tenant", context.TenantId, "attempt", summary.AttemptId)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) summary.ParentSummaryIds.Add(reader.GetString(0)); }
                    using (DbCommand command = Command(connection, null, "SELECT kind,content FROM debate_summary_sections WHERE tenant_id=@tenant AND summary_id=@summary ORDER BY position", P("tenant", context.TenantId, "summary", summary.Id)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) summary.Sections.Add(new SummarySection { Kind = reader.GetString(0), Content = reader.GetString(1) }); }
                }
            }
            return result;
        }
        /// <summary>Exclude derived memory while preserving summaries and original sources for inspection.</summary>
        public async Task ExcludeAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default)
        {
            RequireWrite(context); await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE debate_summaries SET active=0,updated_utc=@now WHERE tenant_id=@tenant AND run_id=@run", token, P("tenant", context.TenantId, "run", runId)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, conversationId, "summary", runId, "excluded", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        private static DomainException Overflow() { return new DomainException(409, "context_budget_exceeded", "The context does not fit. ArgueBot compacts shared and private partitions separately; oversized individual or pinned sources require a larger context budget or revised moderator input. Originals remain intact."); }
    }
}
