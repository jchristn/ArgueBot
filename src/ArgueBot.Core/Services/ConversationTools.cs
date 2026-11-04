namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Invocation-bound evidence retrieval. Caller arguments cannot widen run, time or recipient scope.</summary>
    public sealed class ConversationTools
    {
        private readonly DatabaseDriverBase _Database;
        private readonly RequestContext _Context;
        private readonly DebateRun _Run;
        private readonly string _ParticipantId;
        private readonly long _Through;
        private readonly string _AttemptId;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        /// <summary>Create a tool scope for an accepted invocation and source boundary.</summary>
        public ConversationTools(DatabaseDriverBase database, RequestContext context, DebateRun run, string participantId, long through, string attemptId)
        { _Database = database; _Context = context; _Run = run; _ParticipantId = participantId; _Through = through; _AttemptId = attemptId; }
        /// <summary>Shared typed catalogue for all supported runner transports.</summary>
        public static List<ConversationToolDefinition> Definitions()
        {
            const string schema = """{"type":"object","properties":{"id":{"type":"string"},"ids":{"type":"array","items":{"type":"string"},"maxItems":20},"query":{"type":"string","maxLength":4000},"after":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":20},"offset":{"type":"integer","minimum":0},"contentOffset":{"type":"integer","minimum":0},"length":{"type":"integer","minimum":1,"maximum":8192,"default":2048},"seatId":{"type":"string"},"phase":{"type":"string","enum":["","question","response","synthesis","ratification","compaction","moderator"]},"blocking":{"type":"boolean"}},"additionalProperties":false}""";
            return new List<ConversationToolDefinition>
            {
                new ConversationToolDefinition { Name = "list_turns", Description = "List visible original turns after the after cursor, up to limit (twenty), with IDs and bounded previews. Optional seatId and phase (question, response, synthesis, ratification, compaction or moderator) filters. Pass nextAfter as after for the next page; it is null on the last page.", Schema = schema },
                new ConversationToolDefinition { Name = "get_turn", Description = "Read an original turn by id with offset and length (default 2048, maximum 8192 characters). Follow nextOffset for more.", Schema = schema },
                new ConversationToolDefinition { Name = "search_context", Description = "Case-insensitive literal search of visible evidence for query, with optional seatId, phase and after filters. Returns snippets centered on each match with source IDs, and nextAfter for more.", Schema = schema },
                new ConversationToolDefinition { Name = "get_supporting_details", Description = "Read supporting evidence for up to twenty turn or summary IDs (ids, or id), with attribution, the original sources a summary links to, and gaps for IDs that are not visible. Or search with query. Missing evidence is reported as a gap, never invented.", Schema = schema },
                new ConversationToolDefinition { Name = "get_debate_state", Description = "Read the invocation's roster, phase, question, open proposal, pinned moderator constraints, recorded positions on earlier proposals, and context and roster revisions.", Schema = schema },
                new ConversationToolDefinition { Name = "get_summary", Description = "Read visible memory excerpts and source hashes. Use id and contentOffset with length (default 2048) to follow nextContentOffset. Use offset (0, 100, 200...) for later source pages. Originals remain authoritative.", Schema = schema },
                new ConversationToolDefinition { Name = "request_clarification", Description = "Ask the moderator a question in query. Set blocking to pause for an answer; this never supplies an answer on their behalf.", Schema = schema }
            };
        }
        /// <summary>Validate and execute one bounded tool call, returning safe structured results.</summary>
        public async Task<string> ExecuteAsync(ConversationToolCall call, CancellationToken token = default)
        {
            try
            {
                if (call.ArgumentsJson.Length > 16384) throw new DomainException(400, "invalid_tool_arguments", "Tool arguments exceed the limit.");
                ConversationToolArguments args = JsonSerializer.Deserialize<ConversationToolArguments>(call.ArgumentsJson, _Json) ?? throw new JsonException();
                if (args.Limit < 1 || args.Limit > 20 || args.After < 0 || args.Offset < 0 || args.ContentOffset < 0 || args.Length < 1 || args.Length > 8192 || args.Query.Length > 4000) throw new DomainException(400, "invalid_tool_arguments", "Use bounded cursors, offsets and query text.");
                await _Database.Budgets.ReserveToolAsync(_Context, _Run.Id, _AttemptId, call.CallId, token).ConfigureAwait(false);
                object result;
                switch (call.Name)
                {
                    case "get_turn":
                        Message turn = await VisibleAsync(args.Id, token).ConfigureAwait(false) ?? throw new DomainException(404, "not_found", "Source is unavailable in this invocation.");
                        if (args.Offset > turn.Content.Length || (args.Offset > 0 && args.Offset < turn.Content.Length && char.IsLowSurrogate(turn.Content[args.Offset]))) throw new DomainException(400, "invalid_offset", "Offset exceeds source length.");
                        int end = Math.Min(turn.Content.Length, args.Offset + args.Length);
                        if (end < turn.Content.Length && end > 0 && char.IsHighSurrogate(turn.Content[end - 1])) end--;
                        if (end == args.Offset && end < turn.Content.Length) throw new DomainException(400, "invalid_length", "Request at least two UTF-16 characters to read this Unicode character.");
                        result = new { id = turn.Id, turn.Sequence, turn.Speaker, content = turn.Content.Substring(args.Offset, end - args.Offset), nextOffset = end < turn.Content.Length ? (int?)end : null };
                        break;
                    case "list_turns": result = await ListAsync(args, false, token).ConfigureAwait(false); break;
                    case "search_context":
                        if (string.IsNullOrWhiteSpace(args.Query)) throw new DomainException(400, "missing_query", "Provide query text to search for, or use list_turns to page through turns.");
                        result = await ListAsync(args, true, token).ConfigureAwait(false); break;
                    case "get_supporting_details":
                        if (args.Id.Length == 0 && args.Ids.Count == 0)
                        {
                            if (string.IsNullOrWhiteSpace(args.Query)) throw new DomainException(400, "missing_query", "Provide ids for specific evidence or query text to search for.");
                            ToolPage found = await ListAsync(args, true, token).ConfigureAwait(false);
                            result = new { items = found.Items, found.NextAfter, gaps = found.Items.Count == 0 ? new List<object> { new { query = args.Query, reason = "No visible evidence matches this query." } } : new List<object>() };
                        }
                        else result = await DetailsAsync(args, token).ConfigureAwait(false);
                        break;
                    case "get_debate_state": result = await StateAsync(token).ConfigureAwait(false); break;
                    case "get_summary":
                        if (args.Offset % 100 != 0 || args.ContentOffset > 0 && args.Id.Length == 0) throw new DomainException(400, "invalid_tool_arguments", "Use source offsets in multiples of 100 and a summary ID for subsequent content excerpts.");
                        List<ContextSummary> summaries = await _Database.Memory.EnumerateAsync(_Context, _Run.ConversationId, _Run.Id, token: token, id: args.Id, sourcePageNumber: args.Offset / 100 + 1, recipientId: _ParticipantId, activeOnly: args.Id.Length == 0).ConfigureAwait(false);
                        List<ContextSummary> visible = new List<ContextSummary>();
                        foreach (ContextSummary summary in summaries.Where(summary => summary.ThroughSequence <= _Through && (args.Id.Length == 0 ? summary.Active : summary.Id == args.Id)))
                        {
                            Message original = await _Database.ReadMessageAsync(_Context, _Run.ConversationId, summary.MessageId, token).ConfigureAwait(false);
                            if (original.Sequence <= _Through && (original.RecipientId == null || original.RecipientId == _ParticipantId)) { visible.Add(summary); if (args.Id.Length > 0) break; }
                        }
                        result = visible.Select(summary =>
                        {
                            if (args.ContentOffset > summary.Content.Length || args.ContentOffset > 0 && args.ContentOffset < summary.Content.Length && char.IsLowSurrogate(summary.Content[args.ContentOffset])) throw new DomainException(400, "invalid_offset", "Offset exceeds summary length or splits a Unicode character.");
                            int contentEnd = Math.Min(summary.Content.Length, args.ContentOffset + args.Length);
                            if (contentEnd < summary.Content.Length && contentEnd > 0 && char.IsHighSurrogate(summary.Content[contentEnd - 1])) contentEnd--;
                            if (contentEnd == args.ContentOffset && contentEnd < summary.Content.Length) throw new DomainException(400, "invalid_length", "Request at least two UTF-16 characters to read this Unicode character.");
                            return new { summary.Id, summary.MessageId, summary.AttemptId, summary.RunId, summary.ContextEpoch, summary.ThroughSequence, summary.RecipientId, summary.Active, content = summary.Content.Substring(args.ContentOffset, contentEnd - args.ContentOffset), contentOffset = args.ContentOffset, nextContentOffset = contentEnd < summary.Content.Length ? (int?)contentEnd : null, totalContentCharacters = summary.Content.Length, summary.SourceCount, summary.SourcePageNumber, summary.Sources, summary.ParentSummaryIds };
                        }).ToList(); break;
                    case "request_clarification": result = await ClarifyAsync(call, args, token).ConfigureAwait(false); break;
                    default: throw new DomainException(400, "unknown_tool", "Choose an advertised conversation tool.");
                }
                string json = JsonSerializer.Serialize(new { success = true, result }, _Json);
                if (json.Length > 65536) throw new DomainException(413, "tool_result_limit", "Request a smaller evidence result.");
                return json;
            }
            catch (DomainException exception) when (exception.Code != "tool_budget_exhausted") { return JsonSerializer.Serialize(new { success = false, code = exception.Code, error = exception.Message }, _Json); }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException || exception is NullReferenceException)
            { return "{\"success\":false,\"code\":\"invalid_tool_arguments\",\"error\":\"Use the documented tool schema.\"}"; }
        }
        /// <summary>Read one turn under the invocation's visibility rules: through its source boundary, completed, and public or addressed to this seat. This is the same scope its prompt context uses.</summary>
        private async Task<Message?> VisibleAsync(string id, CancellationToken token)
        {
            if (id.Length == 0 || id.Length > 128) return null;
            Message turn;
            try { turn = await _Database.ReadMessageAsync(_Context, _Run.ConversationId, id, token).ConfigureAwait(false); }
            catch (DomainException error) when (error.Code == "not_found") { return null; }
            if (turn.Sequence > _Through || (turn.State != "completed" && turn.State != "consumed") || (turn.RecipientId != null && turn.RecipientId != _ParticipantId)) return null;
            return turn;
        }

        /// <summary>Page visible turns in sequence order, optionally filtered by seat, phase and a case-insensitive literal match.</summary>
        /// <remarks>Matching runs in the service with ordinal Unicode case folding, so SQLite and PostgreSQL return the same results regardless of database collation. A search scans at most 2000 turns or 8 MiB per call and returns a cursor to continue.</remarks>
        private async Task<ToolPage> ListAsync(ConversationToolArguments args, bool search, CancellationToken token)
        {
            if (args.SeatId.Length > 0 && !_Run.Participants.Any(seat => seat.Id == args.SeatId) && _Run.Orchestrator?.Id != args.SeatId) throw new DomainException(400, "invalid_seat", "Use a seat ID from get_debate_state.");
            string sql = "SELECT m.id,m.sequence_number,m.speaker,m.kind,COALESCE(a.participant_id,'') AS participant,COALESCE(d.purpose,'') AS purpose,length(m.content) AS size," + (search ? "m.content" : "substr(m.content,1,1000) AS content") + " FROM messages m LEFT JOIN generation_attempts a ON a.tenant_id=m.tenant_id AND a.message_id=m.id LEFT JOIN debate_attempts d ON d.tenant_id=a.tenant_id AND d.attempt_id=a.id WHERE m.tenant_id=@tenant AND m.conversation_id=@conversation AND m.sequence_number>@after AND m.sequence_number<=@through AND m.state IN ('completed','consumed') AND (m.recipient_id IS NULL OR m.recipient_id=@profile) AND (@seat='' OR a.participant_id=@seat) AND (@phase='' OR d.purpose=@phase OR (@phase='moderator' AND a.id IS NULL AND m.speaker='user')) ORDER BY m.sequence_number LIMIT @limit";
            ToolPage page = new ToolPage();
            long cursor = args.After; int scanned = 0; long characters = 0;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                while (true)
                {
                    int batch = search ? 200 : args.Limit + 1; int read = 0;
                    using (DbCommand command = Command(connection, null, sql, P("tenant", _Context.TenantId, "conversation", _Run.ConversationId, "after", cursor, "through", _Through, "profile", _ParticipantId, "seat", args.SeatId, "phase", args.Phase, "limit", batch)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            read++;
                            long sequence = Convert.ToInt64(reader["sequence_number"], CultureInfo.InvariantCulture);
                            if (page.Items.Count == args.Limit) { page.NextAfter = page.Items[page.Items.Count - 1].Sequence; return page; }
                            cursor = sequence; scanned++;
                            string content = S(reader, "content"); characters += content.Length;
                            int match = search ? content.IndexOf(args.Query, StringComparison.OrdinalIgnoreCase) : -1;
                            if (!search || match >= 0)
                            {
                                ToolTurn turn = new ToolTurn { Id = S(reader, "id"), Sequence = sequence, Speaker = S(reader, "speaker"), Kind = S(reader, "kind"), SeatId = S(reader, "participant"), Phase = S(reader, "purpose").Length > 0 ? S(reader, "purpose") : S(reader, "speaker") == "user" ? "moderator" : "", Characters = Convert.ToInt64(reader["size"], CultureInfo.InvariantCulture) };
                                if (search) { turn.MatchOffset = match; turn.Snippet = Snippet(content, match, args.Query.Length); }
                                else turn.Snippet = content;
                                turn.Truncated = turn.Snippet.Length < turn.Characters;
                                page.Items.Add(turn);
                            }
                            if (search && (scanned >= 2000 || characters >= 8388608)) { page.NextAfter = cursor; page.Partial = true; return page; }
                        }
                    }
                    if (!search || read < batch) return page;
                }
            }
        }

        /// <summary>A bounded excerpt around a match, never splitting a surrogate pair.</summary>
        private static string Snippet(string content, int match, int length)
        {
            int start = Math.Max(0, match - 300);
            int end = Math.Min(content.Length, match + length + 300);
            if (start > 0 && char.IsLowSurrogate(content[start])) start--;
            if (end < content.Length && end > 0 && char.IsHighSurrogate(content[end - 1])) end++;
            return (start > 0 ? "..." : "") + content.Substring(start, end - start) + (end < content.Length ? "..." : "");
        }

        /// <summary>Resolve requested evidence: visible turns with attribution, the visible originals a summary links to, and explicit gaps.</summary>
        private async Task<object> DetailsAsync(ConversationToolArguments args, CancellationToken token)
        {
            List<string> ids = args.Ids.ToList();
            if (args.Id.Length > 0 && !ids.Contains(args.Id)) ids.Insert(0, args.Id);
            if (ids.Count > 20) throw new DomainException(400, "invalid_tool_arguments", "Request at most twenty IDs at once.");
            List<object> evidence = new List<object>(); List<object> gaps = new List<object>();
            foreach (string id in ids.Distinct(StringComparer.Ordinal))
            {
                Message? turn = await VisibleAsync(id, token).ConfigureAwait(false);
                if (turn == null) { gaps.Add(new { id, reason = "Not visible in this invocation or does not exist." }); continue; }
                List<string> links = new List<string>(); List<string> unavailable = new List<string>();
                if (turn.Kind == "compaction")
                {
                    using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                    using (DbCommand command = Command(connection, null, "SELECT x.message_id FROM debate_summary_sources x JOIN debate_summaries s ON s.tenant_id=x.tenant_id AND s.id=x.summary_id WHERE s.tenant_id=@tenant AND s.message_id=@id ORDER BY x.position LIMIT 100", P("tenant", _Context.TenantId, "id", turn.Id)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) links.Add(reader.GetString(0)); }
                    foreach (string link in links.ToList()) if (await VisibleAsync(link, token).ConfigureAwait(false) == null) { links.Remove(link); unavailable.Add(link); }
                }
                int end = Math.Min(turn.Content.Length, args.Length);
                if (end < turn.Content.Length && end > 0 && char.IsHighSurrogate(turn.Content[end - 1])) end--;
                evidence.Add(new { id = turn.Id, turn.Sequence, turn.Speaker, turn.Kind, attribution = turn.Speaker == "user" ? "moderator" : turn.Speaker, content = turn.Content.Substring(0, end), nextOffset = end < turn.Content.Length ? (int?)end : null, linkedSourceIds = links });
                if (unavailable.Count > 0) gaps.Add(new { id = turn.Id, reason = "Some linked sources are not visible in this invocation.", count = unavailable.Count });
            }
            return new { evidence, gaps };
        }

        /// <summary>Shared debate state for this invocation. Votes on the open proposal are omitted so each seat evaluates it independently.</summary>
        private async Task<object> StateAsync(CancellationToken token)
        {
            List<object> constraints = new List<object>(); List<object> positions = new List<object>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT m.id,m.sequence_number,substr(m.content,1,2000) AS content,length(m.content) AS size FROM debate_interjections i JOIN messages m ON m.tenant_id=i.tenant_id AND m.id=i.message_id WHERE i.tenant_id=@tenant AND i.conversation_id=@conversation AND i.pinned=1 AND m.sequence_number<=@through AND (m.recipient_id IS NULL OR m.recipient_id=@profile) ORDER BY m.sequence_number LIMIT 50", P("tenant", _Context.TenantId, "conversation", _Run.ConversationId, "through", _Through, "profile", _ParticipantId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) constraints.Add(new { id = S(reader, "id"), sequence = Convert.ToInt64(reader["sequence_number"], CultureInfo.InvariantCulture), text = S(reader, "content"), truncated = Convert.ToInt64(reader["size"], CultureInfo.InvariantCulture) > 2000 }); }
                object? closed = await ScalarAsync(connection, null, "SELECT id FROM debate_proposals WHERE tenant_id=@tenant AND run_id=@run AND id<>@open AND active=1 ORDER BY created_utc DESC,id DESC LIMIT 1", token, P("tenant", _Context.TenantId, "run", _Run.Id, "open", _Run.ProposalId)).ConfigureAwait(false);
                if (closed != null && closed is not DBNull)
                {
                    using (DbCommand command = Command(connection, null, "SELECT a.profile_id,a.position,substr(a.rationale,1,1000) AS rationale FROM debate_agreements a WHERE a.tenant_id=@tenant AND a.proposal_id=@proposal AND a.active=1 ORDER BY a.created_utc,a.id", P("tenant", _Context.TenantId, "proposal", Convert.ToString(closed, CultureInfo.InvariantCulture))))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    { while (await reader.ReadAsync(token).ConfigureAwait(false)) positions.Add(new { proposalId = Convert.ToString(closed, CultureInfo.InvariantCulture), seatId = S(reader, "profile_id"), position = S(reader, "position"), rationale = S(reader, "rationale") }); }
                }
            }
            return new { runId = _Run.Id, _Run.Question, _Run.CurrentQuestion, _Run.Phase, _Run.Round, _Run.MaxRounds, _Run.ContextEpoch, _Run.RosterRevision, _Run.ProposalId, _Run.ProposalText, seats = _Run.Participants.Select(seat => new { seat.Id, seat.Name }), constraints, earlierPositions = positions };
        }
        private async Task<ClarificationRequest> ClarifyAsync(ConversationToolCall call, ConversationToolArguments args, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(args.Query)) throw new DomainException(400, "missing_question", "Provide a question for the moderator.");
            ClarificationRequest result = new ClarificationRequest { Id = Ids.New("clr_"), ParticipantId = _ParticipantId, Question = args.Query, Blocking = args.Blocking };
            string callId = _AttemptId + ":" + call.CallId;
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                object? existing = await ScalarAsync(connection, transaction, "SELECT id FROM debate_clarifications WHERE tenant_id=@tenant AND run_id=@run AND call_id=@call", token, P("tenant", _Context.TenantId, "run", _Run.Id, "call", callId)).ConfigureAwait(false);
                // A harness that retries the same request with a new MCP call ID during one attempt gets the existing request, not a second one.
                existing ??= await ScalarAsync(connection, transaction, "SELECT id FROM debate_clarifications WHERE tenant_id=@tenant AND run_id=@run AND profile_id=@profile AND substr(call_id,1,length(@prefix))=@prefix AND question=@question AND blocking=@blocking AND status='open' ORDER BY created_utc LIMIT 1", token, P("tenant", _Context.TenantId, "run", _Run.Id, "profile", _ParticipantId, "prefix", _AttemptId + ":", "question", args.Query, "blocking", args.Blocking ? 1 : 0)).ConfigureAwait(false);
                if (existing != null) { result.Id = Convert.ToString(existing, CultureInfo.InvariantCulture)!; return; }
                await DatabaseDriverBase.ExecuteAsync(connection, transaction, "INSERT INTO debate_clarifications(id,tenant_id,conversation_id,run_id,profile_id,call_id,question,blocking,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@run,@profile,@call,@question,@blocking,@now,@now)", token, P("id", result.Id, "tenant", _Context.TenantId, "conversation", _Run.ConversationId, "run", _Run.Id, "profile", _ParticipantId, "call", callId, "question", args.Query, "blocking", args.Blocking ? 1 : 0)).ConfigureAwait(false);
                await EventAsync(connection, transaction, _Context.TenantId, _Run.ConversationId, "clarification", result.Id, "open", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return result;
        }
    }
}
