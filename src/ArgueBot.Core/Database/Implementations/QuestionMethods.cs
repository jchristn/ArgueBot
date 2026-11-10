namespace ArgueBot.Core.Database.Implementations
{
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
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Question storage. Requests, questions, options, answers and selections are separate rows; resolution is first-writer-wins under the service write lock.</summary>
    public sealed class QuestionMethods : IQuestionMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal QuestionMethods(DatabaseDriverBase database) { _Database = database; }

        /// <inheritdoc />
        public async Task<QuestionAskResult> CreateAsync(RequestContext context, QuestionAsk ask, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(ask);
            if (ask.Visibility != "panel" && ask.Visibility != "self") throw new DomainException(400, "invalid_question", "visibility is panel or self.", "visibility");
            ask.Context = QuestionRules.ValidateContext(ask.Context);
            QuestionRules.Validate(ask.Questions);
            string digest = Digest(ask);
            QuestionAskResult result = new QuestionAskResult();
            string id = Ids.New("qrq_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                object? existing = await ScalarAsync(connection, transaction, "SELECT id FROM question_requests WHERE tenant_id=@tenant AND run_id=@run AND call_id=@call", token, P("tenant", context.TenantId, "run", ask.RunId, "call", ask.CallId)).ConfigureAwait(false);
                // A harness that retries the same call with a new call ID during one attempt gets the open request it already made.
                existing ??= await ScalarAsync(connection, transaction, "SELECT id FROM question_requests WHERE tenant_id=@tenant AND run_id=@run AND attempt_id=@attempt AND asker_profile_id=@asker AND digest=@digest AND status='open' ORDER BY created_utc LIMIT 1", token, P("tenant", context.TenantId, "run", ask.RunId, "attempt", ask.AttemptId, "asker", ask.AskerId, "digest", digest)).ConfigureAwait(false);
                if (existing != null)
                {
                    result.Existing = true;
                    result.Request = await LoadAsync(connection, transaction, context.TenantId, Convert.ToString(existing, CultureInfo.InvariantCulture)!, token).ConfigureAwait(false) ?? throw NotFound();
                    return;
                }
                long turn = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM question_requests WHERE tenant_id=@tenant AND run_id=@run AND attempt_id=@attempt", token, P("tenant", context.TenantId, "run", ask.RunId, "attempt", ask.AttemptId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (turn >= ask.MaxPerTurn) throw new DomainException(409, "question_limit", "This turn has asked the most questions the debate allows (" + ask.MaxPerTurn.ToString(CultureInfo.InvariantCulture) + "). Continue with what you know and state your assumptions.");
                int counted = await ExecuteAsync(connection, transaction, "UPDATE debate_policies SET question_requests=question_requests+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@run AND question_requests<@limit", token, P("tenant", context.TenantId, "run", ask.RunId, "limit", ask.MaxPerRun)).ConfigureAwait(false);
                if (counted != 1) throw new DomainException(409, "question_limit", "The debate has asked the most questions it allows (" + ask.MaxPerRun.ToString(CultureInfo.InvariantCulture) + "). Continue with what you know and state your assumptions.");
                bool warning = QuestionRules.LooksLikeCredentialRequest(ask.Context, ask.Questions);
                await ExecuteAsync(connection, transaction, "INSERT INTO question_requests(id,tenant_id,conversation_id,run_id,attempt_id,call_id,digest,asker_profile_id,asker_name,asker_role,phase,round,context,wait,visibility,status,hold_state,credential_warning,expires_utc,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@run,@attempt,@call,@digest,@asker,@name,@role,@phase,@round,@context,@wait,@visibility,'open','none',@warning,@expires,@now,@now)", token, P("id", id, "tenant", context.TenantId, "conversation", ask.ConversationId, "run", ask.RunId, "attempt", ask.AttemptId, "call", ask.CallId, "digest", digest, "asker", ask.AskerId, "name", ask.AskerName, "role", ask.AskerRole, "phase", ask.Phase, "round", ask.Round, "context", ask.Context, "wait", ask.Wait ? 1 : 0, "visibility", ask.Visibility, "warning", warning ? 1 : 0, "expires", ask.ExpiresUtc.HasValue ? ask.ExpiresUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : "")).ConfigureAwait(false);
                for (int position = 0; position < ask.Questions.Count; position++)
                {
                    Question question = ask.Questions[position];
                    question.Id = Ids.New("qst_");
                    await ExecuteAsync(connection, transaction, "INSERT INTO questions(id,tenant_id,request_id,position,question_key,header,prompt,kind,allow_other,min_selections,max_selections,max_length,placeholder,required,created_utc,updated_utc) VALUES(@id,@tenant,@request,@position,@key,@header,@prompt,@kind,@other,@min,@max,@length,@placeholder,@required,@now,@now)", token, P("id", question.Id, "tenant", context.TenantId, "request", id, "position", position, "key", question.Key, "header", question.Header, "prompt", question.Prompt, "kind", question.Kind, "other", question.AllowOther ? 1 : 0, "min", question.MinSelections, "max", question.MaxSelections, "length", question.MaxLength, "placeholder", question.Placeholder, "required", question.Required ? 1 : 0)).ConfigureAwait(false);
                    for (int index = 0; index < question.Options.Count; index++)
                    {
                        QuestionOption option = question.Options[index];
                        option.Id = Ids.New("qop_");
                        await ExecuteAsync(connection, transaction, "INSERT INTO question_options(id,tenant_id,question_id,position,option_key,label,description,recommended,created_utc,updated_utc) VALUES(@id,@tenant,@question,@position,@key,@label,@description,@recommended,@now,@now)", token, P("id", option.Id, "tenant", context.TenantId, "question", question.Id, "position", index, "key", option.Key, "label", option.Label, "description", option.Description, "recommended", option.Recommended ? 1 : 0)).ConfigureAwait(false);
                    }
                }
                await EventAsync(connection, transaction, context.TenantId, ask.ConversationId, "question", id, "opened", token).ConfigureAwait(false);
                result.Request = await LoadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false) ?? throw NotFound();
            }, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<QuestionRequest> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(id);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return await LoadAsync(connection, null, context.TenantId, id, token).ConfigureAwait(false) ?? throw NotFound();
        }

        /// <inheritdoc />
        public async Task<QuestionPage> EnumerateAsync(RequestContext context, QuestionFilter filter, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(filter);
            if (filter.PageNumber < 1) throw new DomainException(400, "invalid_page", "Use a page number of 1 or more.", "pageNumber");
            if (filter.PageSize < 1 || filter.PageSize > 100) throw new DomainException(400, "invalid_page", "Use a page size from 1 through 100.", "pageSize");
            if (filter.Scope != "mine" && filter.Scope != "tenant") throw new DomainException(400, "invalid_filter", "scope is mine or tenant.", "scope");
            string[] statuses = { "", "open", "answered", "skipped", "expired", "cancelled", "resolved" };
            if (!statuses.Contains(filter.Status, StringComparer.Ordinal)) throw new DomainException(400, "invalid_filter", "status is open, answered, skipped, expired, cancelled or resolved.", "status");
            StringBuilder where = new StringBuilder("r.tenant_id=@tenant");
            Dictionary<string, object?> values = P("tenant", context.TenantId, "user", context.UserId, "conversation", filter.ConversationId, "run", filter.RunId, "status", filter.Status, "limit", filter.PageSize, "offset", ((long)filter.PageNumber - 1) * filter.PageSize);
            if (filter.Status == "resolved") where.Append(" AND r.status<>'open'");
            else if (filter.Status.Length > 0) where.Append(" AND r.status=@status");
            if (filter.ConversationId.Length > 0) where.Append(" AND r.conversation_id=@conversation");
            if (filter.RunId.Length > 0) where.Append(" AND r.run_id=@run");
            if (filter.Scope == "mine") where.Append(" AND c.user_id=@user");
            QuestionPage page = new QuestionPage { PageNumber = filter.PageNumber, PageSize = filter.PageSize };
            List<string> ids = new List<string>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM question_requests r JOIN conversations c ON c.tenant_id=r.tenant_id AND c.id=r.conversation_id WHERE " + where, token, values).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT r.id FROM question_requests r JOIN conversations c ON c.tenant_id=r.tenant_id AND c.id=r.conversation_id WHERE " + where + " ORDER BY CASE WHEN r.status='open' THEN 0 ELSE 1 END,r.created_utc DESC,r.id LIMIT @limit OFFSET @offset", values))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) ids.Add(reader.GetString(0)); }
                foreach (string id in ids)
                {
                    QuestionRequest? request = await LoadAsync(connection, null, context.TenantId, id, token).ConfigureAwait(false);
                    if (request != null) page.Items.Add(request);
                }
            }
            return page;
        }

        /// <inheritdoc />
        public async Task<List<QuestionRequest>> RunRequestsAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default)
        {
            await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            List<QuestionRequest> result = new List<QuestionRequest>();
            List<string> ids = new List<string>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT id FROM question_requests WHERE tenant_id=@tenant AND conversation_id=@conversation AND run_id=@run ORDER BY created_utc,id LIMIT 500", P("tenant", context.TenantId, "conversation", conversationId, "run", runId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) ids.Add(reader.GetString(0)); }
                foreach (string id in ids)
                {
                    QuestionRequest? request = await LoadAsync(connection, null, context.TenantId, id, token).ConfigureAwait(false);
                    if (request != null) result.Add(request);
                }
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<QuestionSummary> SummaryAsync(RequestContext context, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            QuestionSummary summary = new QuestionSummary();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                summary.OpenTenant = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM question_requests WHERE tenant_id=@tenant AND status='open'", token, P("tenant", context.TenantId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                summary.OpenMine = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM question_requests r JOIN conversations c ON c.tenant_id=r.tenant_id AND c.id=r.conversation_id WHERE r.tenant_id=@tenant AND r.status='open' AND c.user_id=@user", token, P("tenant", context.TenantId, "user", context.UserId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                summary.WaitingRuns = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM runs WHERE tenant_id=@tenant AND state='awaiting_input'", token, P("tenant", context.TenantId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                summary.NewestId = Convert.ToString(await ScalarAsync(connection, null, "SELECT id FROM question_requests WHERE tenant_id=@tenant AND status='open' ORDER BY created_utc DESC,id LIMIT 1", token, P("tenant", context.TenantId)).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
            }
            return summary;
        }

        /// <inheritdoc />
        public async Task<bool> HasOpenWaitingAsync(RequestContext context, string runId, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(runId);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return await ScalarAsync(connection, null, "SELECT id FROM question_requests WHERE tenant_id=@tenant AND run_id=@run AND wait=1 AND status='open' LIMIT 1", token, P("tenant", context.TenantId, "run", runId)).ConfigureAwait(false) != null;
        }

        /// <inheritdoc />
        public async Task<QuestionRequest> AnswerAsync(RequestContext context, string id, QuestionAnswerRequest request, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(request);
            if (request.IdempotencyKey.Length > 128) throw new DomainException(400, "invalid_answer", "Keep the idempotency key to 128 characters.", "idempotencyKey");
            QuestionRequest? result = null;
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                QuestionRequest current = await LoadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false) ?? throw NotFound();
                if (await AlreadyResolvedAsync(connection, transaction, context, current, request.IdempotencyKey, "answered", token).ConfigureAwait(false)) { result = current; return; }
                List<QuestionAnswer> answers = new List<QuestionAnswer>();
                HashSet<string> named = new HashSet<string>(StringComparer.Ordinal);
                foreach (QuestionAnswerInput input in request.Answers)
                {
                    Question? target = current.Questions.FirstOrDefault(question => (input.QuestionId.Length > 0 && question.Id == input.QuestionId) || (input.QuestionId.Length == 0 && input.Key.Length > 0 && question.Key == input.Key));
                    if (target == null) throw new DomainException(400, "invalid_answer", "Each answer names a question of this request by questionId or key.", "answers");
                    if (!named.Add(target.Id)) throw new DomainException(400, "invalid_answer", "Answer each question once.", "answers");
                }
                for (int index = 0; index < current.Questions.Count; index++)
                {
                    Question question = current.Questions[index];
                    QuestionAnswerInput? input = request.Answers.FirstOrDefault(item => item.QuestionId == question.Id || (item.QuestionId.Length == 0 && item.Key == question.Key));
                    QuestionAnswer answer = QuestionRules.Conform(question, input, "answers[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                    answer.AnsweredByUserId = context.UserId;
                    question.Answer = answer;
                    answers.Add(answer);
                }
                if (answers.All(answer => answer.Skipped)) throw new DomainException(400, "invalid_answer", "Answer at least one question, or skip the whole request.", "answers");
                await ResolveAsync(connection, transaction, context, current, "answered", "", "", request.IdempotencyKey, token).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "question_answered", current, token).ConfigureAwait(false);
                result = await LoadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return result ?? throw NotFound();
        }

        /// <inheritdoc />
        public async Task<QuestionRequest> SkipAsync(RequestContext context, string id, QuestionSkipRequest request, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(request);
            string note = request.Note.Trim();
            if (note.Length > QuestionRules.MaxNote) throw new DomainException(400, "invalid_answer", "Keep the note to 2000 characters.", "note");
            if (note.Contains('\0', StringComparison.Ordinal)) throw new DomainException(400, "invalid_answer", "The note cannot contain NUL characters.", "note");
            if (request.IdempotencyKey.Length > 128) throw new DomainException(400, "invalid_answer", "Keep the idempotency key to 128 characters.", "idempotencyKey");
            QuestionRequest? result = null;
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                QuestionRequest current = await LoadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false) ?? throw NotFound();
                if (await AlreadyResolvedAsync(connection, transaction, context, current, request.IdempotencyKey, "skipped", token).ConfigureAwait(false)) { result = current; return; }
                foreach (Question question in current.Questions) question.Answer = new QuestionAnswer { Skipped = true, AnsweredByUserId = context.UserId };
                await ResolveAsync(connection, transaction, context, current, "skipped", "skipped_by_user", note, request.IdempotencyKey, token).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "question_skipped", current, token).ConfigureAwait(false);
                result = await LoadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return result ?? throw NotFound();
        }

        /// <inheritdoc />
        public async Task DeleteAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(id);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                QuestionRequest current = await LoadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false) ?? throw NotFound();
                if (current.Status == "open") throw new DomainException(409, "question_open", "Answer or skip this question before deleting it. An agent may still be waiting for it.", "questionId");
                if (!current.Deletable) throw new DomainException(409, "active_run", "This question's debate is still running and may use the answer in later turns. Delete it after the debate finishes.", "questionId");
                await ExecuteAsync(connection, transaction, "DELETE FROM question_requests WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "question_deleted", current, token).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, current.ConversationId, "question", id, "deleted", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetHoldStateAsync(RequestContext context, string id, string holdState, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(holdState);
            if (holdState != "held" && holdState != "parked" && holdState != "none") throw new ArgumentOutOfRangeException(nameof(holdState), "Hold state is held, parked or none.");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                QuestionRequest current = await LoadAsync(connection, transaction, context.TenantId, id, token).ConfigureAwait(false) ?? throw NotFound();
                if (current.HoldState == holdState) return;
                await ExecuteAsync(connection, transaction, "UPDATE question_requests SET hold_state=@state,updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("state", holdState, "tenant", context.TenantId, "id", id)).ConfigureAwait(false);
                if (current.Status == "open" && holdState != "none") await EventAsync(connection, transaction, context.TenantId, current.ConversationId, "question", id, holdState, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task LinkResumeAsync(RequestContext context, string id, string attemptId, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(attemptId);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE question_requests SET resume_attempt_id=@attempt,hold_state='none',updated_utc=@now WHERE tenant_id=@tenant AND id=@id", token, P("attempt", attemptId, "tenant", context.TenantId, "id", id)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<QuestionRequest>> ExpireDueAsync(CancellationToken token = default)
        {
            List<QuestionRequest> expired = new List<QuestionRequest>();
            string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            List<string[]> due = new List<string[]>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT tenant_id,id FROM question_requests WHERE status='open' AND expires_utc<>'' AND expires_utc<=@due ORDER BY expires_utc LIMIT 100", P("due", now)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) due.Add(new[] { reader.GetString(0), reader.GetString(1) }); }
            foreach (string[] item in due)
            {
                await _Database.WriteAsync(async (connection, transaction) =>
                {
                    QuestionRequest? current = await LoadAsync(connection, transaction, item[0], item[1], token).ConfigureAwait(false);
                    if (current == null || current.Status != "open") return;
                    foreach (Question question in current.Questions) question.Answer = new QuestionAnswer { Skipped = true };
                    RequestContext system = new RequestContext { TenantId = item[0], UserId = "", IsAuthenticated = true, PrincipalType = "Service" };
                    await ResolveAsync(connection, transaction, system, current, "expired", "expired", "", "", token).ConfigureAwait(false);
                    QuestionRequest? stored = await LoadAsync(connection, transaction, item[0], item[1], token).ConfigureAwait(false);
                    if (stored != null) expired.Add(stored);
                }, token).ConfigureAwait(false);
            }
            return expired;
        }

        /// <inheritdoc />
        public async Task<int> CancelOpenAsync(RequestContext context, string runId, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(runId);
            int changed = 0;
            await _Database.WriteAsync(async (connection, transaction) => { changed = await CancelOpenAsync(connection, transaction, context.TenantId, runId, token).ConfigureAwait(false); }, token).ConfigureAwait(false);
            return changed;
        }

        /// <summary>Cancel open requests of a run inside an existing write transaction.</summary>
        internal static async Task<int> CancelOpenAsync(DbConnection connection, DbTransaction transaction, string tenantId, string runId, CancellationToken token)
        {
            List<string[]> open = new List<string[]>();
            using (DbCommand command = Command(connection, transaction, "SELECT id,conversation_id FROM question_requests WHERE tenant_id=@tenant AND run_id=@run AND status='open'", P("tenant", tenantId, "run", runId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) open.Add(new[] { reader.GetString(0), reader.GetString(1) }); }
            foreach (string[] item in open)
            {
                await ExecuteAsync(connection, transaction, "UPDATE question_requests SET status='cancelled',hold_state='none',resolution_reason='run_stopped',resolved_utc=@now,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND status='open'", token, P("tenant", tenantId, "id", item[0])).ConfigureAwait(false);
                await EventAsync(connection, transaction, tenantId, item[1], "question", item[0], "cancelled", token).ConfigureAwait(false);
            }
            return open.Count;
        }

        /// <inheritdoc />
        public async Task<QuestionEventPage> ReadEventsAsync(RequestContext context, string after, int limit, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(after);
            if (limit < 1 || limit > 200) throw new DomainException(400, "invalid_page", "Read 1 through 200 changes at a time.", "limit");
            QuestionEventPage page = new QuestionEventPage { Next = after };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                string time; string eventId;
                if (after.Length == 0)
                {
                    using (DbCommand command = Command(connection, null, "SELECT created_utc,id FROM events WHERE tenant_id=@tenant AND kind='question' ORDER BY created_utc DESC,id DESC LIMIT 1", P("tenant", context.TenantId)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (!await reader.ReadAsync(token).ConfigureAwait(false)) { page.Next = "0|"; return page; }
                        time = reader.GetString(0); eventId = reader.GetString(1);
                    }
                    page.Next = time + "|" + eventId;
                    return page;
                }
                string[] parts = after.Split('|');
                if (parts.Length != 2) throw new DomainException(400, "invalid_cursor", "Use the cursor from a previous read.", "after");
                time = parts[0]; eventId = parts[1];
                using (DbCommand command = Command(connection, null, "SELECT id,conversation_id,entity_id,text,created_utc FROM events WHERE tenant_id=@tenant AND kind='question' AND (created_utc>@time OR (created_utc=@time AND id>@event)) ORDER BY created_utc,id LIMIT @limit", P("tenant", context.TenantId, "time", time, "event", eventId, "limit", limit)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        QuestionEvent entry = new QuestionEvent { ConversationId = S(reader, "conversation_id"), RequestId = S(reader, "entity_id"), Status = S(reader, "text"), CreatedUtc = Time(S(reader, "created_utc")) ?? DateTime.MinValue };
                        entry.Cursor = S(reader, "created_utc") + "|" + S(reader, "id");
                        page.Items.Add(entry); page.Next = entry.Cursor;
                    }
                }
            }
            return page;
        }

        /// <inheritdoc />
        public async Task<int> ParkHeldAsync(CancellationToken token = default)
        {
            int changed = 0;
            await _Database.WriteAsync(async (connection, transaction) =>
            { changed = await ExecuteAsync(connection, transaction, "UPDATE question_requests SET hold_state='parked',updated_utc=@now WHERE status='open' AND wait=1 AND hold_state<>'parked'", token).ConfigureAwait(false); }, token).ConfigureAwait(false);
            return changed;
        }

        private static async Task<bool> AlreadyResolvedAsync(DbConnection connection, DbTransaction transaction, RequestContext context, QuestionRequest current, string key, string status, CancellationToken token)
        {
            if (current.Status == "open") return false;
            string stored = Convert.ToString(await ScalarAsync(connection, transaction, "SELECT resolution_key FROM question_requests WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", current.Id)).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
            if (key.Length > 0 && stored == key && current.ResolvedByUserId == context.UserId && current.Status == status) return true;
            throw new DomainException(409, "question_already_resolved", current.Status == "answered" ? "This question was already answered." : current.Status == "skipped" ? "This question was already skipped." : current.Status == "expired" ? "This question expired before it was answered." : "This question was cancelled because its debate stopped.");
        }

        private static async Task ResolveAsync(DbConnection connection, DbTransaction transaction, RequestContext context, QuestionRequest current, string status, string reason, string note, string key, CancellationToken token)
        {
            int changed = await ExecuteAsync(connection, transaction, "UPDATE question_requests SET status=@status,resolution_reason=@reason,resolution_note=@note,resolved_by_user_id=@user,resolution_key=@key,resolved_utc=@now,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND status='open'", token, P("status", status, "reason", reason, "note", note, "user", context.UserId, "key", key, "tenant", context.TenantId, "id", current.Id)).ConfigureAwait(false);
            if (changed != 1) throw new DomainException(409, "question_already_resolved", "This question was resolved by someone else.");
            foreach (Question question in current.Questions)
            {
                QuestionAnswer answer = question.Answer ?? new QuestionAnswer { Skipped = true, AnsweredByUserId = context.UserId };
                string answerId = Ids.New("qan_");
                await ExecuteAsync(connection, transaction, "INSERT INTO question_answers(id,tenant_id,question_id,skipped,text,other_text,confirmed,answered_by_user_id,created_utc,updated_utc) VALUES(@id,@tenant,@question,@skipped,@text,@other,@confirmed,@user,@now,@now)", token, P("id", answerId, "tenant", context.TenantId, "question", question.Id, "skipped", answer.Skipped ? 1 : 0, "text", answer.Text, "other", answer.OtherText, "confirmed", answer.Confirmed.HasValue ? (answer.Confirmed.Value ? 1 : 0) : -1, "user", context.UserId)).ConfigureAwait(false);
                for (int position = 0; position < answer.Selected.Count; position++)
                {
                    QuestionOption option = question.Options.First(item => item.Key == answer.Selected[position]);
                    await ExecuteAsync(connection, transaction, "INSERT INTO question_answer_selections(id,tenant_id,answer_id,option_id,position,created_utc,updated_utc) VALUES(@id,@tenant,@answer,@option,@position,@now,@now)", token, P("id", Ids.New("qas_"), "tenant", context.TenantId, "answer", answerId, "option", option.Id, "position", position)).ConfigureAwait(false);
                }
            }
            current.Status = status; current.ResolutionReason = reason; current.ResolutionNote = note;
            string messageId = Ids.New("msg_");
            await ExecuteAsync(connection, transaction, "INSERT INTO messages(id,tenant_id,conversation_id,run_id,sequence_number,speaker,kind,content,recipient_id,state,created_utc,updated_utc) SELECT @id,@tenant,@conversation,@run,COALESCE(MAX(sequence_number),0)+1,'user','answer',@text,@recipient,'completed',@now,@now FROM messages WHERE conversation_id=@conversation", token, P("id", messageId, "tenant", context.TenantId, "conversation", current.ConversationId, "run", current.RunId, "text", QuestionRules.Describe(current), "recipient", current.Visibility == "self" ? current.AskerId : null)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE question_requests SET answer_message_id=@message WHERE tenant_id=@tenant AND id=@id", token, P("message", messageId, "tenant", context.TenantId, "id", current.Id)).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "UPDATE conversations SET revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@conversation", token, P("tenant", context.TenantId, "conversation", current.ConversationId)).ConfigureAwait(false);
            await EventAsync(connection, transaction, context.TenantId, current.ConversationId, "message", messageId, "", token).ConfigureAwait(false);
            await EventAsync(connection, transaction, context.TenantId, current.ConversationId, "question", current.Id, status, token).ConfigureAwait(false);
        }

        private static Task AuditAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string operation, QuestionRequest request, CancellationToken token)
        {
            return AuditLog.WriteAsync(connection, transaction, new AuditEvent { TenantId = context.TenantId, UserId = context.UserId, Operation = operation, Outcome = "success", ResourceType = "question", ResourceId = request.Id, Reason = "Conversation " + request.ConversationId + ", run " + request.RunId, PrincipalType = context.PrincipalType, SessionId = context.SessionId, CredentialId = context.CredentialId, AuthScheme = context.AuthScheme, CorrelationId = context.CorrelationId }, token);
        }

        internal static async Task<QuestionRequest?> LoadAsync(DbConnection connection, DbTransaction? transaction, string tenantId, string id, CancellationToken token)
        {
            QuestionRequest? request = null;
            using (DbCommand command = Command(connection, transaction, "SELECT r.*,c.title AS conversation_title,c.user_id AS owner_user_id,COALESCE(u.state,'') AS run_state FROM question_requests r JOIN conversations c ON c.tenant_id=r.tenant_id AND c.id=r.conversation_id LEFT JOIN runs u ON u.tenant_id=r.tenant_id AND u.id=r.run_id WHERE r.tenant_id=@tenant AND r.id=@id", P("tenant", tenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                request = new QuestionRequest
                {
                    TenantId = S(reader, "tenant_id"), OwnerUserId = S(reader, "owner_user_id"), Id = S(reader, "id"), ConversationId = S(reader, "conversation_id"), ConversationTitle = S(reader, "conversation_title"), RunId = S(reader, "run_id"), AttemptId = S(reader, "attempt_id"),
                    AskerId = S(reader, "asker_profile_id"), AskerName = S(reader, "asker_name"), AskerRole = S(reader, "asker_role"), Phase = S(reader, "phase"), Round = Convert.ToInt64(reader["round"], CultureInfo.InvariantCulture),
                    Context = S(reader, "context"), Wait = Convert.ToInt64(reader["wait"], CultureInfo.InvariantCulture) != 0, Visibility = S(reader, "visibility"), Status = S(reader, "status"), HoldState = S(reader, "hold_state"),
                    ResolutionReason = S(reader, "resolution_reason"), ResolutionNote = S(reader, "resolution_note"), ResolvedByUserId = S(reader, "resolved_by_user_id"), AnswerMessageId = S(reader, "answer_message_id"), ResumeAttemptId = S(reader, "resume_attempt_id"), RunState = S(reader, "run_state"),
                    CredentialWarning = Convert.ToInt64(reader["credential_warning"], CultureInfo.InvariantCulture) != 0, ExpiresUtc = Time(S(reader, "expires_utc")), ResolvedUtc = Time(S(reader, "resolved_utc")), CreatedUtc = Time(S(reader, "created_utc")) ?? DateTime.MinValue
                };
            }
            Dictionary<string, Question> byId = new Dictionary<string, Question>(StringComparer.Ordinal);
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM questions WHERE tenant_id=@tenant AND request_id=@id ORDER BY position", P("tenant", tenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    Question question = new Question { Id = S(reader, "id"), Key = S(reader, "question_key"), Header = S(reader, "header"), Prompt = S(reader, "prompt"), Kind = S(reader, "kind"), AllowOther = Convert.ToInt64(reader["allow_other"], CultureInfo.InvariantCulture) != 0, MinSelections = Convert.ToInt32(reader["min_selections"], CultureInfo.InvariantCulture), MaxSelections = Convert.ToInt32(reader["max_selections"], CultureInfo.InvariantCulture), MaxLength = Convert.ToInt32(reader["max_length"], CultureInfo.InvariantCulture), Placeholder = S(reader, "placeholder"), Required = Convert.ToInt64(reader["required"], CultureInfo.InvariantCulture) != 0 };
                    request.Questions.Add(question); byId[question.Id] = question;
                }
            }
            Dictionary<string, QuestionOption> options = new Dictionary<string, QuestionOption>(StringComparer.Ordinal);
            using (DbCommand command = Command(connection, transaction, "SELECT o.* FROM question_options o JOIN questions q ON q.tenant_id=o.tenant_id AND q.id=o.question_id WHERE o.tenant_id=@tenant AND q.request_id=@id ORDER BY o.question_id,o.position", P("tenant", tenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    QuestionOption option = new QuestionOption { Id = S(reader, "id"), Key = S(reader, "option_key"), Label = S(reader, "label"), Description = S(reader, "description"), Recommended = Convert.ToInt64(reader["recommended"], CultureInfo.InvariantCulture) != 0 };
                    if (byId.TryGetValue(S(reader, "question_id"), out Question? owner)) owner.Options.Add(option);
                    options[option.Id] = option;
                }
            }
            Dictionary<string, QuestionAnswer> answers = new Dictionary<string, QuestionAnswer>(StringComparer.Ordinal);
            using (DbCommand command = Command(connection, transaction, "SELECT a.* FROM question_answers a JOIN questions q ON q.tenant_id=a.tenant_id AND q.id=a.question_id WHERE a.tenant_id=@tenant AND q.request_id=@id", P("tenant", tenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    long confirmed = Convert.ToInt64(reader["confirmed"], CultureInfo.InvariantCulture);
                    QuestionAnswer answer = new QuestionAnswer { Skipped = Convert.ToInt64(reader["skipped"], CultureInfo.InvariantCulture) != 0, Text = S(reader, "text"), OtherText = S(reader, "other_text"), Confirmed = confirmed < 0 ? null : confirmed != 0, AnsweredByUserId = S(reader, "answered_by_user_id") };
                    if (byId.TryGetValue(S(reader, "question_id"), out Question? owner)) owner.Answer = answer;
                    answers[S(reader, "id")] = answer;
                }
            }
            using (DbCommand command = Command(connection, transaction, "SELECT s.answer_id,s.option_id FROM question_answer_selections s JOIN question_answers a ON a.tenant_id=s.tenant_id AND a.id=s.answer_id JOIN questions q ON q.tenant_id=a.tenant_id AND q.id=a.question_id WHERE s.tenant_id=@tenant AND q.request_id=@id ORDER BY s.answer_id,s.position", P("tenant", tenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    if (answers.TryGetValue(reader.GetString(0), out QuestionAnswer? answer) && options.TryGetValue(reader.GetString(1), out QuestionOption? option)) { answer.Selected.Add(option.Key); answer.SelectedLabels.Add(option.Label); }
                }
            }
            return request;
        }

        private static string Digest(QuestionAsk ask)
        {
            string text = JsonSerializer.Serialize(new { ask.Context, ask.Wait, ask.Visibility, questions = ask.Questions.Select(question => new { question.Key, question.Header, question.Prompt, question.Kind, options = question.Options.Select(option => new { option.Key, option.Label }) }) });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }

        private static DateTime? Time(string value) { return value.Length == 0 ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }

        private static DomainException NotFound() { return new DomainException(404, "not_found", "The question was not found."); }
    }
}
