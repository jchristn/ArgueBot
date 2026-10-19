namespace ArgueBot.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Backups;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database.Implementations;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Observability;
    using ArgueBot.Core.Services;

    /// <summary>Tenant-scoped relational persistence. Operations serialize writes; readers use independent connections.</summary>
    public abstract class DatabaseDriverBase : IDisposable, IConversationMethods, IMessageMethods, IEventMethods
    {
        private readonly DatabaseSettings _Settings;
        private readonly string? _BackupDirectory;
        private readonly SemaphoreSlim _Writer = new SemaphoreSlim(1, 1);
        private bool _Disposed;
        private DbConnection? _OwnedWriter;
        private readonly CancellationTokenSource _Ownership = new CancellationTokenSource();
        /// <summary>Cancelled when a provider's exclusive writer session is lost. The host must drain and stop its execution workers.</summary>
        public CancellationToken OwnershipToken { get { return _Ownership.Token; } }

        /// <summary>Runner configuration methods.</summary>
        public IProfileMethods Profiles { get; }
        /// <summary>Tenant-scoped identity administration.</summary>
        public IUserMethods Users { get; }
        /// <summary>Execution checkpoint methods.</summary>
        public IRunMethods Runs { get; }
        /// <summary>Tenant-aware HTTP request history and retention.</summary>
        public IHistoryMethods History { get; }
        /// <summary>Global administrator recovery inventory and operation lifecycle.</summary>
        public IBackupMethods Backups { get; }
        /// <summary>Append-only security accounting and scoped inspection.</summary>
        public IAuditMethods Audit { get; }
        /// <summary>Service-owned generation checkpoints and exact context provenance.</summary>
        public IAttemptMethods Attempts { get; }
        /// <summary>Effective running storage provider.</summary>
        public DatabaseTypeEnum ProviderType { get { return _Settings.Type; } }


        /// <summary>Create a driver. Initialization must run before any other operation.</summary>
        protected DatabaseDriverBase(DatabaseSettings settings, string? backupDirectory = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Settings = settings; _BackupDirectory = backupDirectory;
            Profiles = new ProfileMethods(this);
            Users = new UserMethods(this);
            Runs = new RunMethods(this);
            History = new HistoryMethods(this);
            Backups = new BackupMethods(this);
            Audit = new AuditMethods(this);
            Attempts = new AttemptMethods(this);
        }

        /// <summary>Initialize idempotently and reconcile interrupted generations. Never downgrade a future schema.</summary>
        public Task InitializeAsync(CancellationToken token = default) { return InitializeCoreAsync(false, token); }

        /// <summary>Initialize only an empty destination database after acquiring its exclusive owner. Refuses all existing user tables before any schema mutation; intended for isolated recovery.</summary>
        public Task InitializeEmptyAsync(CancellationToken token = default) { return InitializeCoreAsync(true, token); }

        private async Task InitializeCoreAsync(bool requireEmpty, CancellationToken token)
        {
            await _Writer.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_OwnedWriter == null)
                {
                    _OwnedWriter = await AcquireOwnedWriterAsync(token).ConfigureAwait(false);
                    if (_OwnedWriter != null) _OwnedWriter.StateChange += WriterStateChanged;
                }
                using (WriterConnectionLease lease = await OpenWriterAsync(token).ConfigureAwait(false))
                {
                    DbConnection connection = lease.Connection;
                    if (requireEmpty)
                    {
                        long tables = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'", token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        if (tables != 0) throw new DomainException(409, "restore_target_not_empty", "Recovery requires an empty destination database. Choose a newly provisioned database.");
                    }
                    await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", token).ConfigureAwait(false);
                    long version = 0;
                    object? exists = await ScalarAsync(connection, null, "SELECT name FROM sqlite_master WHERE name='schema_migrations'", token).ConfigureAwait(false);
                    if (exists != null)
                    {
                        version = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COALESCE(MAX(version),0) FROM schema_migrations", token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        if (version > DatabaseSchema.CurrentVersion) throw new InvalidDataException("Database was created by a newer ArgueBot schema; restore a compatible backup.");
                        if (version < DatabaseSchema.CurrentVersion) await BeforeUpgradeAsync(version, token).ConfigureAwait(false);
                    }
                    using (DbTransaction transaction = connection.BeginTransaction())
                    {
                        if (exists == null)
                        {
                            await ExecuteAsync(connection, transaction, SchemaDefinition.Create, token).ConfigureAwait(false);
                        }
                        foreach (SchemaMigration migration in SchemaMigrations.All)
                        {
                            if (migration.Version <= version) continue;
                            if (exists != null)
                            {
                                foreach (string statement in migration.Statements)
                                {
                                    if (statement.StartsWith("ALTER TABLE ", StringComparison.Ordinal) && statement.Contains(" ADD COLUMN ", StringComparison.Ordinal))
                                    {
                                        string tableName = statement.Split(' ')[2]; string column = statement.Split(' ')[5]; bool present = false;
                                        using (DbCommand inspect = Command(connection, transaction, "SELECT * FROM " + tableName + " WHERE 1=0"))
                                        using (DbDataReader columns = await inspect.ExecuteReaderAsync(token).ConfigureAwait(false))
                                        { for (int index = 0; index < columns.FieldCount; index++) if (columns.GetName(index) == column) present = true; }
                                        if (present) continue;
                                    }
                                    if (migration.Version == 2)
                                    {
                                        string table = statement.Contains("messages", StringComparison.Ordinal) ? "messages" : "events";
                                        bool renamed = false;
                                        using (DbCommand inspect = Command(connection, transaction, "SELECT * FROM " + table + " WHERE 1=0"))
                                        using (DbDataReader columns = await inspect.ExecuteReaderAsync(token).ConfigureAwait(false))
                                        { for (int index = 0; index < columns.FieldCount; index++) if (columns.GetName(index) == "sequence_number") renamed = true; }
                                        if (renamed) continue;
                                    }
                                    await ExecuteAsync(connection, transaction, statement, token).ConfigureAwait(false);
                                }
                            }
                            if (migration.Version == 11) await PasswordVerifierMigration.ApplyAsync(connection, transaction, token).ConfigureAwait(false);
                            await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO schema_migrations(version,applied_utc) VALUES(@version,@now)", token, P("version", migration.Version)).ConfigureAwait(false);
                        }
                        await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO tenants(id,name,created_utc,updated_utc) VALUES('ten_local','Local',@now,@now)", token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO users(id,tenant_id,name,role,is_admin,is_tenant_admin,protected,created_utc,updated_utc) VALUES('usr_local','ten_local','Local owner','admin',1,1,1,@now,@now)", token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, "UPDATE messages SET state='interrupted',updated_utc=@now WHERE state='streaming'; UPDATE runs SET state='paused',outcome='interrupted',revision=revision+1,updated_utc=@now WHERE state IN ('agent_turn','window','queued','pausing','interrupting')", token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, "UPDATE runs SET state='cancelled',outcome='stopped',revision=revision+1,updated_utc=@now WHERE state='stopping'", token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, "UPDATE generation_attempts SET state='interrupted',error_code='startup_uncertain',updated_utc=@now WHERE state IN ('pending','streaming')", token).ConfigureAwait(false);
                        transaction.Commit();
                    }
                }
            }
            finally { _Writer.Release(); }
        }

        /// <summary>Create a conversation for the authenticated user.</summary>
        public async Task<Conversation> CreateConversationAsync(RequestContext context, string title, CancellationToken token = default)
        {
            RequireWrite(context);
            if (string.IsNullOrWhiteSpace(title) || title.Length > 4000) throw new DomainException(400, "invalid_title", "Provide a title between 1 and 4000 characters.");
            string id = Ids.New("cnv_");
            await WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO conversations(id,tenant_id,user_id,title,created_utc,updated_utc) VALUES(@id,@tenant,@user,@title,@now,@now)", token, P("id", id, "tenant", context.TenantId, "user", context.UserId, "title", title.Trim())).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, id, "conversation_created", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadConversationAsync(context, id, token).ConfigureAwait(false);
        }

        /// <summary>Read an owned conversation. Foreign resources return not found.</summary>
        public async Task<Conversation> ReadConversationAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT conversations.*,(SELECT COALESCE(MAX(e.sequence_number),0) FROM events e WHERE e.tenant_id=conversations.tenant_id AND e.conversation_id=conversations.id) AS event_head FROM conversations WHERE tenant_id=@tenant AND user_id=@user AND id=@id", P("tenant", context.TenantId, "user", context.UserId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Conversation not found.");
                return ReadConversation(reader);
            }
        }

        /// <summary>Search all owned history using escaped literal substring matching and bounded pagination.</summary>
        public async Task<Page> EnumerateConversationsAsync(RequestContext context, string search = "", bool archived = false, int page = 1, int size = 25, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            if (page < 1 || size < 1 || size > 100) throw new DomainException(400, "invalid_page", "Page must be positive and page size between 1 and 100.");
            string filter = "tenant_id=@tenant AND user_id=@user AND archived=@archived AND (instr(lower(title),lower(@search))>0 OR id IN (SELECT conversation_id FROM messages WHERE tenant_id=@tenant AND instr(lower(content),lower(@search))>0))";
            Dictionary<string, object?> values = P("tenant", context.TenantId, "user", context.UserId, "archived", archived ? 1 : 0, "search", search, "limit", size, "offset", checked((long)(page - 1) * size));
            Page result = new Page { PageNumber = page, PageSize = size };
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            {
                result.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM conversations WHERE " + filter, token, values).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT conversations.*,(SELECT COALESCE(MAX(e.sequence_number),0) FROM events e WHERE e.tenant_id=conversations.tenant_id AND e.conversation_id=conversations.id) AS event_head FROM conversations WHERE " + filter + " ORDER BY pinned DESC,updated_utc DESC,id DESC LIMIT @limit OFFSET @offset", values))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Items.Add(ReadConversation(reader)); }
            }
            return result;
        }

        /// <summary>Update title, pin and archive with an optimistic revision.</summary>
        public async Task<Conversation> UpdateConversationAsync(RequestContext context, string id, ConversationRequest request, CancellationToken token = default)
        {
            RequireWrite(context);
            await ReadConversationAsync(context, id, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 4000 || request.Revision == null) throw new DomainException(400, "invalid_update", "A title and expected revision are required.");
            await WriteAsync(async (connection, transaction) =>
            {
                int changed = await ExecuteAsync(connection, transaction, "UPDATE conversations SET title=@title,pinned=@pin,archived=@archive,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND user_id=@user AND id=@id AND revision=@revision", token, P("title", request.Title.Trim(), "pin", request.Pinned ? 1 : 0, "archive", request.Archived ? 1 : 0, "tenant", context.TenantId, "user", context.UserId, "id", id, "revision", request.Revision)).ConfigureAwait(false);
                if (changed == 0) throw new DomainException(409, "revision_conflict", "Conversation changed. Reload before saving.");
                await EventAsync(connection, transaction, context.TenantId, id, "conversation_updated", id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadConversationAsync(context, id, token).ConfigureAwait(false);
        }

        /// <summary>Delete an owned conversation only after its work has stopped.</summary>
        public async Task DeleteConversationAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireWrite(context);
            await ReadConversationAsync(context, id, token).ConfigureAwait(false);
            await WriteAsync(async (connection, transaction) =>
            {
                long busy = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM runs WHERE tenant_id=@tenant AND conversation_id=@id AND state NOT IN ('completed','cancelled','failed')", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (busy > 0) throw new DomainException(409, "active_run", "Stop the active or paused run before deleting this conversation.");
                await ExecuteAsync(connection, transaction, "DELETE FROM conversations WHERE tenant_id=@tenant AND user_id=@user AND id=@id", token, P("tenant", context.TenantId, "user", context.UserId, "id", id)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <summary>Read ordered visible history. Private directives are available to the owning user only.</summary>
        public async Task<List<Message>> ReadMessagesAsync(RequestContext context, string conversationId, CancellationToken token = default)
        {
            await ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            List<Message> messages = new List<Message>();
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM messages WHERE tenant_id=@tenant AND conversation_id=@id ORDER BY sequence_number", P("tenant", context.TenantId, "id", conversationId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false)) messages.Add(ReadMessage(reader, conversationId));
            }
            return messages;
        }

        /// <summary>Read an individual full message through the owned-conversation boundary. No other message content is materialized.</summary>
        public async Task<Message> ReadMessageAsync(RequestContext context, string conversationId, string messageId, CancellationToken token = default)
        {
            await ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM messages WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@message", P("tenant", context.TenantId, "conversation", conversationId, "message", messageId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Message not found.");
                return ReadMessage(reader, conversationId);
            }
        }
        /// <summary>Read bounded Unicode previews with exact counts and a stable membership boundary. A boundary captures message membership, not immutable streaming content.</summary>
        public async Task<MessagePage> ReadMessagePageAsync(RequestContext context, string conversationId, int pageNumber = 1, int pageSize = 25, long? throughSequence = null, bool tail = false, int previewCodePoints = 4096, CancellationToken token = default)
        {
            await ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || previewCodePoints < 128 || previewCodePoints > 16384 || throughSequence < 0) throw new DomainException(400, "invalid_page", "Use a positive page, size one through one hundred, valid preview bound and nonnegative membership boundary.");
            MessagePage page = new MessagePage { PageNumber = pageNumber, PageSize = pageSize };
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            {
                page.LatestSequence = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COALESCE(MAX(sequence_number),0) FROM messages WHERE tenant_id=@tenant AND conversation_id=@conversation", token, P("tenant", context.TenantId, "conversation", conversationId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                page.ThroughSequence = throughSequence ?? page.LatestSequence;
                if (page.ThroughSequence > page.LatestSequence) throw new DomainException(400, "invalid_cursor", "The message membership boundary is ahead of this conversation.");
                const string scope = "tenant_id=@tenant AND conversation_id=@conversation AND sequence_number<=@through";
                Dictionary<string, object?> values = P("tenant", context.TenantId, "conversation", conversationId, "through", page.ThroughSequence);
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM messages WHERE " + scope, token, values).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (tail) page.PageNumber = checked((int)Math.Max(1, (page.TotalCount + pageSize - 1) / pageSize));
                values.Add("limit", pageSize); values.Add("offset", checked((long)(page.PageNumber - 1) * pageSize)); values.Add("preview", previewCodePoints);
                using (DbCommand command = Command(connection, null, "SELECT id,run_id,sequence_number,speaker,kind,substr(content,1,@preview) AS content,length(CAST(content AS BLOB)) AS content_bytes,recipient_id,state,created_utc FROM messages WHERE " + scope + " ORDER BY sequence_number LIMIT @limit OFFSET @offset", values))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        Message message = ReadMessage(reader, conversationId); message.ContentBytes = Convert.ToInt64(reader["content_bytes"], CultureInfo.InvariantCulture);
                        message.ContentTruncated = Encoding.UTF8.GetByteCount(message.Content) < message.ContentBytes;
                        page.Items.Add(message);
                    }
                }
            }
            return page;
        }

        /// <summary>Prepare exact model context without materializing the full transcript. Metadata bounds eligible rows and UTF-8 payload size before content reads; the final UTF-16 check never truncates or silently omits a source.</summary>
        public async Task<List<Message>> ReadGenerationContextAsync(RequestContext context, string conversationId, string participantId, int characterBudget, int maxMessages, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(participantId);
            if (characterBudget < 0 || characterBudget > 16777216 || maxMessages < 1 || maxMessages > 10000) throw new ArgumentOutOfRangeException(nameof(characterBudget));
            await ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            List<string> identifiers = new List<string>(); List<Message> messages = new List<Message>(); long bytes = 0;
            const string scope = "tenant_id=@tenant AND conversation_id=@conversation AND state='completed' AND (recipient_id IS NULL OR recipient_id=@participant)";
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            {
                using (DbCommand command = Command(connection, null, "SELECT id,length(CAST(content AS BLOB)) AS payload_bytes FROM messages WHERE " + scope + " ORDER BY sequence_number LIMIT @limit", P("tenant", context.TenantId, "conversation", conversationId, "participant", participantId, "limit", maxMessages + 1)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        identifiers.Add(reader.GetString(0)); bytes = checked(bytes + Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture));
                        // UTF-8 requires at most three bytes per UTF-16 code unit. This is a conservative memory bound, not a token estimate.
                        if (identifiers.Count > maxMessages || bytes > (long)characterBudget * 3) throw ContextBudgetExceeded();
                    }
                }
                if (identifiers.Count == 0) return messages;
                Dictionary<string, object?> values = P("tenant", context.TenantId, "conversation", conversationId, "participant", participantId);
                List<string> placeholders = new List<string>();
                for (int index = 0; index < identifiers.Count; index++) { string name = "source" + index.ToString(CultureInfo.InvariantCulture); placeholders.Add("@" + name); values.Add(name, identifiers[index]); }
                using (DbCommand command = Command(connection, null, "SELECT * FROM messages WHERE " + scope + " AND id IN (" + string.Join(",", placeholders) + ") ORDER BY sequence_number", values))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    long characters = 0;
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        Message message = ReadMessage(reader, conversationId); characters = checked(characters + message.Content.Length);
                        if (characters > characterBudget) throw ContextBudgetExceeded();
                        messages.Add(message);
                    }
                }
                if (messages.Count != identifiers.Count) throw new DomainException(409, "context_changed", "Selected context changed during preparation. Refresh and retry.");
            }
            return messages;
        }
        private static DomainException ContextBudgetExceeded()
        { return new DomainException(409, "context_budget_exceeded", "The selected conversation context exceeds the configured budget. No model was invoked and no transcript text was dropped."); }
        private static Message ReadMessage(DbDataReader reader, string conversationId)
        { return new Message { Id = S(reader, "id"), ConversationId = conversationId, RunId = N(reader, "run_id"), Sequence = Convert.ToInt64(reader["sequence_number"], CultureInfo.InvariantCulture), Speaker = S(reader, "speaker"), Kind = S(reader, "kind"), Content = S(reader, "content"), RecipientId = N(reader, "recipient_id"), State = S(reader, "state"), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) }; }

        /// <summary>Recognize an exact note/directive retry across run-state and revision changes.</summary>
        public async Task<bool> IsDuplicateMessageAsync(RequestContext context, Message message, string commandKey, CancellationToken token = default)
        {
            RequireWrite(context);
            await ReadConversationAsync(context, message.ConversationId, token).ConfigureAwait(false);
            ValidateMessageKey(commandKey);
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            { return await MessageReceiptAsync(connection, null, context, message, commandKey, token).ConfigureAwait(false) != null; }
        }
        private static void ValidateMessageKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 200) throw new DomainException(400, "invalid_key", "Use an idempotency key between 1 and 200 characters.");
        }
        private static string MessageDigest(Message message)
        { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Kind + "\n" + message.RecipientId + "\n" + message.Content))); }
        private static async Task<string?> MessageReceiptAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, Message message, string key, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT r.digest,r.result_id,m.run_id,m.id AS message_id FROM command_receipts r LEFT JOIN messages m ON m.tenant_id=r.tenant_id AND m.id=r.result_id WHERE r.tenant_id=@tenant AND r.user_id=@user AND r.conversation_id=@conversation AND r.command_key=@key", P("tenant", context.TenantId, "user", context.UserId, "conversation", message.ConversationId, "key", key)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                if (reader.IsDBNull(reader.GetOrdinal("message_id")) || S(reader, "digest") != MessageDigest(message) || N(reader, "run_id") != message.RunId) throw new DomainException(409, "idempotency_conflict", "Command key was already used with different content or run.");
                return S(reader, "result_id");
            }
        }
        /// <summary>Append a message transactionally with a durable event and optional deduplication receipt.</summary>
        public async Task<Message> AppendMessageAsync(RequestContext context, Message message, string commandKey = "", CancellationToken token = default)
        {
            RequireWrite(context);
            await ReadConversationAsync(context, message.ConversationId, token).ConfigureAwait(false);
            if (message.Content.Length > 1048576) throw new DomainException(400, "message_too_large", "Message exceeds one MiB.");
            ArgumentNullException.ThrowIfNull(commandKey);
            if (commandKey.Length > 0) ValidateMessageKey(commandKey);
            string digest = MessageDigest(message);
            message.Id = Ids.New("msg_"); message.CreatedUtc = DateTime.UtcNow;
            await WriteAsync(async (connection, transaction) =>
            {
                if (commandKey.Length > 0)
                {
                    string? existing = await MessageReceiptAsync(connection, transaction, context, message, commandKey, token).ConfigureAwait(false);
                    if (existing != null) { message.Id = existing; return; }
                }
                message.Sequence = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COALESCE(MAX(sequence_number),0)+1 FROM messages WHERE conversation_id=@id", token, P("id", message.ConversationId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await ExecuteAsync(connection, transaction, "INSERT INTO messages(id,tenant_id,conversation_id,run_id,sequence_number,speaker,kind,content,recipient_id,state,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@run,@seq,@speaker,@kind,@content,@recipient,@state,@now,@now)", token, P("id", message.Id, "tenant", context.TenantId, "conversation", message.ConversationId, "run", message.RunId, "seq", message.Sequence, "speaker", message.Speaker, "kind", message.Kind, "content", message.Content, "recipient", message.RecipientId, "state", message.State)).ConfigureAwait(false);
                if (commandKey.Length > 0) await ExecuteAsync(connection, transaction, "INSERT INTO command_receipts(id,tenant_id,user_id,conversation_id,command_key,digest,result_id,created_utc,updated_utc) VALUES(@id,@tenant,@user,@conversation,@key,@digest,@result,@now,@now)", token, P("id", Ids.New("cmd_"), "tenant", context.TenantId, "user", context.UserId, "conversation", message.ConversationId, "key", commandKey, "digest", digest, "result", message.Id)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE conversations SET updated_utc=@now,revision=revision+1 WHERE id=@id AND tenant_id=@tenant", token, P("id", message.ConversationId, "tenant", context.TenantId)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, message.ConversationId, "message", message.Id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadMessageAsync(context, message.ConversationId, message.Id, token).ConfigureAwait(false);
        }

        /// <summary>Persist a full accumulated streaming snapshot and its durable delta in one transaction.</summary>
        public async Task UpdateMessageAsync(RequestContext context, Message message, string delta, CancellationToken token = default)
        {
            RequireWrite(context);
            ArgumentNullException.ThrowIfNull(delta);
            if (delta.Length > message.Content.Length || !message.Content.EndsWith(delta, StringComparison.Ordinal)) throw new DomainException(400, "invalid_stream_delta", "The fragment must be a suffix of its accumulated message.");
            await ReadConversationAsync(context, message.ConversationId, token).ConfigureAwait(false);
            await WriteAsync(async (connection, transaction) =>
            {
                string currentContent; string currentState;
                using (DbCommand inspect = Command(connection, transaction, "SELECT content,state FROM messages WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@id", P("tenant", context.TenantId, "conversation", message.ConversationId, "id", message.Id)))
                using (DbDataReader reader = await inspect.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Message not found.");
                    currentContent = S(reader, "content"); currentState = S(reader, "state");
                }
                if (currentContent == message.Content && currentState == message.State) return;
                if (currentState != "streaming") throw new DomainException(409, "message_finalized", "Finalized message content and state are immutable; directive consumption belongs to atomic attempt completion.");
                string prefix = message.Content.Substring(0, message.Content.Length - delta.Length);
                if (currentContent != prefix) throw new DomainException(409, "stream_revision_conflict", "The fragment does not extend the committed message. Reconcile its snapshot.");
                await ExecuteAsync(connection, transaction, "UPDATE messages SET content=@content,state=@state,updated_utc=@now WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@id", token, P("content", message.Content, "state", message.State, "tenant", context.TenantId, "conversation", message.ConversationId, "id", message.Id)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, message.ConversationId, message.State == "streaming" ? "delta" : "message_end", message.Id, delta, message.Content.Length - delta.Length, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <summary>Read durable ordered events after a cursor, bounded to 1000 per call.</summary>
        public async Task<List<DurableEvent>> ReadEventsAsync(RequestContext context, string conversationId, long after = 0, int pageSize = 1000, CancellationToken token = default)
        {
            await ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            if (after < 0 || pageSize < 1 || pageSize > 1000) throw new DomainException(400, "invalid_cursor", "Use a nonnegative event cursor and a replay size from one through one thousand.");
            List<DurableEvent> events = new List<DurableEvent>();
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            {
                long minimum; long maximum;
                using (DbCommand bounds = Command(connection, null, "SELECT COALESCE(MIN(sequence_number),0),COALESCE(MAX(sequence_number),0) FROM events WHERE tenant_id=@tenant AND conversation_id=@id", P("tenant", context.TenantId, "id", conversationId)))
                using (DbDataReader reader = await bounds.ExecuteReaderAsync(token).ConfigureAwait(false))
                { await reader.ReadAsync(token).ConfigureAwait(false); minimum = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture); maximum = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture); }
                if (after > maximum) throw new DomainException(400, "invalid_cursor", "The event cursor is ahead of this conversation. Refresh its snapshot.");
                if (after != 0 && after < minimum - 1) throw EventHistoryGap();
                using (DbCommand command = Command(connection, null, "SELECT * FROM events WHERE tenant_id=@tenant AND conversation_id=@id AND sequence_number>@after ORDER BY sequence_number LIMIT @limit", P("tenant", context.TenantId, "id", conversationId, "after", after, "limit", pageSize)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    long expected = after == 0 ? 0 : after + 1;
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        long sequence = Convert.ToInt64(reader["sequence_number"], CultureInfo.InvariantCulture);
                        if (expected == 0) expected = sequence;
                        if (sequence != expected) throw EventHistoryGap();
                        expected++;
                        events.Add(new DurableEvent { ConversationId = conversationId, Sequence = sequence, Kind = S(reader, "kind"), EntityId = S(reader, "entity_id"), Text = S(reader, "text"), TextOffset = Convert.ToInt64(reader["text_offset"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) });
                    }
                }
            }
            return events;
        }

        private DomainException EventHistoryGap()
        {
            ProductTelemetry.ReplayGaps.Add(1, new KeyValuePair<string, object?>("provider", _Settings.Type.ToString()));
            return new DomainException(409, "event_history_gap", "The event cursor expired. Capture the current conversation event head, reload snapshots, then reconnect after that head.");
        }
        /// <summary>Prune contiguous replay prefixes in bounded metadata batches. Always retain the latest event so a later commit cannot reuse an old sequence.</summary>
        public async Task<long> PruneEventsAsync(DateTime cutoffUtc, int maxEventsPerConversation, int batchSize = 100, CancellationToken token = default)
        {
            if (cutoffUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Use a UTC retention cutoff.", nameof(cutoffUtc));
            if (maxEventsPerConversation < 1 || maxEventsPerConversation > 1000000 || batchSize < 1 || batchSize > 1000) throw new ArgumentOutOfRangeException(nameof(maxEventsPerConversation));
            string cursor = ""; long removed = 0;
            while (true)
            {
                Dictionary<string, string> scopes = new Dictionary<string, string>(StringComparer.Ordinal);
                using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
                using (DbCommand command = Command(connection, null, "SELECT conversation_id,tenant_id FROM events WHERE conversation_id>@cursor GROUP BY conversation_id,tenant_id HAVING COUNT(*)>@maximum OR (COUNT(*)>1 AND MIN(created_utc)<@cutoff) ORDER BY conversation_id LIMIT @limit", P("cursor", cursor, "limit", batchSize, "maximum", maxEventsPerConversation, "cutoff", cutoffUtc.ToString("O", CultureInfo.InvariantCulture))))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) { cursor = reader.GetString(0); scopes.Add(cursor, reader.GetString(1)); } }
                foreach (KeyValuePair<string, string> scope in scopes)
                {
                    await WriteAsync(async (connection, transaction) =>
                    {
                        Dictionary<string, object?> values = P("tenant", scope.Value, "conversation", scope.Key, "cutoff", cutoffUtc.ToString("O", CultureInfo.InvariantCulture), "count", maxEventsPerConversation);
                        const string where = "tenant_id=@tenant AND conversation_id=@conversation";
                        long maximum = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COALESCE(MAX(sequence_number),0) FROM events WHERE " + where, token, values).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        long age = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COALESCE(MAX(sequence_number),0) FROM events WHERE " + where + " AND created_utc<@cutoff", token, values).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        object? counted = await ScalarAsync(connection, transaction, "SELECT sequence_number FROM events WHERE " + where + " ORDER BY sequence_number DESC LIMIT 1 OFFSET @count", token, values).ConfigureAwait(false);
                        long through = Math.Min(maximum - 1, Math.Max(age, counted == null ? 0 : Convert.ToInt64(counted, CultureInfo.InvariantCulture)));
                        if (through <= 0) return;
                        values.Add("through", through);
                        removed += await ExecuteAsync(connection, transaction, "DELETE FROM events WHERE " + where + " AND sequence_number<=@through", token, values).ConfigureAwait(false);
                    }, token).ConfigureAwait(false);
                }
                if (scopes.Count < batchSize) break;
            }
            if (removed != 0) ProductTelemetry.EventsPruned.Add(removed, new KeyValuePair<string, object?>("provider", _Settings.Type.ToString()));
            return removed;
        }

        /// <summary>Protect an existing schema with a logical snapshot before applying ordered upgrades.</summary>
        protected virtual async Task BeforeUpgradeAsync(long version, CancellationToken token)
        {
            if (_BackupDirectory == null) throw new InvalidOperationException("Configure a protected backup directory before upgrading an existing server database.");
            Directory.CreateDirectory(_BackupDirectory);
            HomePermissions.Directory(_BackupDirectory);
            await new LogicalSnapshot(this, _Settings.Type).ExportAsync(Path.Combine(_BackupDirectory, "before-schema-" + version + "-" + Guid.NewGuid().ToString("N") + ".arguedb.jsonl"), token).ConfigureAwait(false);
        }
        /// <summary>Create a consistent driver backup. SQLite overrides this with its native online file format.</summary>
        public virtual Task BackupAsync(string destination, CancellationToken token = default)
        { return ExportSnapshotAsync(destination, token); }
        /// <summary>Stream a portable, checksummed relational snapshot under the service writer gate.</summary>
        public async Task ExportSnapshotAsync(string destination, CancellationToken token = default)
        {
            await _Writer.WaitAsync(token).ConfigureAwait(false);
            try { await new LogicalSnapshot(this, _Settings.Type).ExportAsync(destination, token).ConfigureAwait(false); }
            finally { _Writer.Release(); }
        }
        /// <summary>Restore a fully validated snapshot atomically. The caller must own the application home exclusively and stop all execution first.</summary>
        public async Task RestoreSnapshotAsync(string source, CancellationToken token = default)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "arguebot-restore-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(temporary);
            else Directory.CreateDirectory(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            HomePermissions.Directory(temporary);
            string stable = Path.Combine(temporary, "snapshot.jsonl");
            try
            {
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
                using (FileStream output = new FileStream(stable, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    HomePermissions.File(stable);
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                }
                await LogicalSnapshot.ValidateAsync(stable, token).ConfigureAwait(false);
                await WriteAsync((connection, transaction) => LogicalSnapshot.RestoreAsync(connection, transaction, stable, token), token).ConfigureAwait(false);
            }
            finally { Directory.Delete(temporary, true); }
        }

        /// <summary>Invalidate restored sessions before publishing an offline recovery home. The caller must hold exclusive maintenance ownership.</summary>
        public Task RevokeRestoredSessionsAsync(CancellationToken token = default)
        {
            return WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE backup_records SET status='unavailable',updated_utc=@now; UPDATE backup_operations SET state='failed',error_code='recovery_interrupted',updated_utc=@now WHERE state IN ('queued','running'); UPDATE auth_sessions SET active=0,updated_utc=@now WHERE active=1", token).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "INSERT INTO audit_events(id,operation,outcome,created_utc,updated_utc) VALUES(@id,'recovery_sessions','revoked',@now,@now)", token, P("id", Ids.New("aud_"))).ConfigureAwait(false);
            }, token);
        }

        /// <summary>Require a validated principal with exactly one nonempty tenant and user binding before accessing domain data.</summary>
        public static void RequireAuthenticated(RequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!context.IsAuthenticated || string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.UserId)) throw new DomainException(401, "unauthenticated", "A tenant-bound authenticated principal is required.");
        }

        /// <summary>Reject unauthenticated or read-only mutation requests.</summary>
        public static void RequireWrite(RequestContext context)
        {
            RequireAuthenticated(context);
            if (!HasWritePermission(context)) throw new DomainException(403, "forbidden", "This operation requires write permission.");
        }
        internal static bool HasWritePermission(RequestContext context)
        { return context.IsAdmin || context.IsTenantAdmin || context.Role == "admin" || context.Role == "operator"; }

        /// <summary>Optionally acquire an exclusive provider session that is retained for all writes until disposal. Default providers use transient connections.</summary>
        protected virtual Task<DbConnection?> AcquireOwnedWriterAsync(CancellationToken token) { return Task.FromResult<DbConnection?>(null); }
        private async Task<WriterConnectionLease> OpenWriterAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); _Ownership.Token.ThrowIfCancellationRequested();
            if (_OwnedWriter != null)
            {
                if (_OwnedWriter.State != ConnectionState.Open) throw new InvalidOperationException("The exclusive database writer session was lost. Restart the service before writing again.");
                return new WriterConnectionLease(_OwnedWriter, false);
            }
            return new WriterConnectionLease(await OpenAsync(token).ConfigureAwait(false), true);
        }
        private void WriterStateChanged(object? sender, StateChangeEventArgs change)
        {
            if (!_Disposed && change.CurrentState != ConnectionState.Open) _Ownership.Cancel();
        }

        /// <summary>Apply provider-specific connection initialization after a successful open.</summary>
        protected virtual void OnConnectionOpened(DbConnection connection) { }

        internal async Task<DbConnection> OpenAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            _Ownership.Token.ThrowIfCancellationRequested();
            DbConnection connection = DatabaseDriverFactory.CreateConnection(_Settings);
            try { await connection.OpenAsync(token).ConfigureAwait(false); OnConnectionOpened(connection); return connection; }
            catch { connection.Dispose(); throw; }
        }

        internal async Task WriteAsync(Func<DbConnection, DbTransaction, Task> action, CancellationToken token)
        {
            long started = Stopwatch.GetTimestamp(); string outcome = "failed";
            using (Activity? activity = ProductTelemetry.Activities.StartActivity("storage.write"))
            {
                activity?.SetTag("db.system", _Settings.Type.ToString());
                await _Writer.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    using (WriterConnectionLease lease = await OpenWriterAsync(token).ConfigureAwait(false))
                    {
                        using (DbTransaction transaction = lease.Connection.BeginTransaction())
                        { await action(lease.Connection, transaction).ConfigureAwait(false); transaction.Commit(); outcome = "success"; }
                    }
                }
                catch { activity?.SetStatus(ActivityStatusCode.Error); throw; }
                finally
                {
                    _Writer.Release();
                    TagList tags = new TagList { { "provider", _Settings.Type.ToString() }, { "outcome", outcome } };
                    ProductTelemetry.StorageWrites.Add(1, tags); ProductTelemetry.StorageDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
                }
            }
        }

        internal static async Task EventAsync(DbConnection connection, DbTransaction transaction, string tenant, string conversation, string kind, string entity, string text, CancellationToken token)
        { await EventAsync(connection, transaction, tenant, conversation, kind, entity, text, 0, token).ConfigureAwait(false); }
        internal static async Task EventAsync(DbConnection connection, DbTransaction transaction, string tenant, string conversation, string kind, string entity, string text, long textOffset, CancellationToken token)
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO events(id,tenant_id,conversation_id,sequence_number,kind,entity_id,text,text_offset,created_utc,updated_utc) SELECT @id,@tenant,@conversation,COALESCE(MAX(sequence_number),0)+1,@kind,@entity,@text,@offset,@now,@now FROM events WHERE conversation_id=@conversation", token, P("id", Ids.New("evt_"), "tenant", tenant, "conversation", conversation, "kind", kind, "entity", entity, "text", text, "offset", textOffset)).ConfigureAwait(false);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        { DbParameter parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }

        private static Conversation ReadConversation(DbDataReader reader)
        {
            return new Conversation { Id = S(reader, "id"), TenantId = S(reader, "tenant_id"), UserId = S(reader, "user_id"), Title = S(reader, "title"), Pinned = Convert.ToInt64(reader["pinned"], CultureInfo.InvariantCulture) != 0, Archived = Convert.ToInt64(reader["archived"], CultureInfo.InvariantCulture) != 0, Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), LastEventSequence = Convert.ToInt64(reader["event_head"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
        }
        internal static string S(DbDataReader reader, string name) { return reader.GetString(reader.GetOrdinal(name)); }
        internal static string? N(DbDataReader reader, string name) { return reader.IsDBNull(reader.GetOrdinal(name)) ? null : S(reader, name); }
        internal static Dictionary<string, object?> P(params object?[] values)
        {
            Dictionary<string, object?> result = new Dictionary<string, object?>();
            for (int index = 0; index < values.Length; index += 2) result.Add((string)values[index]!, values[index + 1]);
            return result;
        }
        internal static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, Dictionary<string, object?>? values = null)
        {
            DbCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = QueryDialect.Translate(connection, sql);
            AddParameter(command, "@now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            if (values != null) foreach (KeyValuePair<string, object?> pair in values) AddParameter(command, "@" + pair.Key, pair.Value ?? DBNull.Value);
            return command;
        }
        internal static async Task<int> ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken token, Dictionary<string, object?>? values = null)
        { using (DbCommand command = Command(connection, transaction, sql, values)) { return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); } }
        internal static async Task<object?> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken token, Dictionary<string, object?>? values = null)
        { using (DbCommand command = Command(connection, transaction, sql, values)) { return await command.ExecuteScalarAsync(token).ConfigureAwait(false); } }

        /// <summary>Release synchronization resources after all operations have stopped.</summary>
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
        /// <summary>Release owned managed synchronization resources.</summary>
        protected virtual void Dispose(bool disposing) { if (_Disposed) return; _Disposed = true; if (disposing) { _OwnedWriter?.Dispose(); _Ownership.Dispose(); _Writer.Dispose(); } }
    }
}
