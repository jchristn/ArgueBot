namespace ArgueBot.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
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
        private readonly SemaphoreSlim _Writer = new SemaphoreSlim(1, 1);
        private bool _Disposed;
        /// <summary>Runner configuration methods.</summary>
        public ProfileMethods Profiles { get; }
        /// <summary>Execution checkpoint methods.</summary>
        public RunMethods Runs { get; }
        /// <summary>Tenant-aware HTTP request history and retention.</summary>
        public HistoryMethods History { get; }


        /// <summary>Create a driver. Initialization must run before any other operation.</summary>
        protected DatabaseDriverBase(DatabaseSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Settings = settings;
            Profiles = new ProfileMethods(this);
            Runs = new RunMethods(this);
            History = new HistoryMethods(this);
        }

        /// <summary>Initialize idempotently and reconcile interrupted generations. Never downgrade a future schema.</summary>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            await _Writer.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
                {
                    await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", token).ConfigureAwait(false);
                    long version = 0;
                    object? exists = await ScalarAsync(connection, null, "SELECT name FROM sqlite_master WHERE name='schema_migrations'", token).ConfigureAwait(false);
                    if (exists != null)
                    {
                        version = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COALESCE(MAX(version),0) FROM schema_migrations", token).ConfigureAwait(false), CultureInfo.InvariantCulture);
                        if (version > 3) throw new InvalidDataException("Database was created by a newer ArgueBot schema; restore a compatible backup.");
                        if (version < 3) await BeforeUpgradeAsync(version, token).ConfigureAwait(false);
                    }
                    using (DbTransaction transaction = connection.BeginTransaction())
                    {
                        await ExecuteAsync(connection, transaction, SchemaDefinition.Create, token).ConfigureAwait(false);
                        if (version == 1)
                        {
                            await ExecuteAsync(connection, transaction, "ALTER TABLE messages RENAME COLUMN sequence TO sequence_number; ALTER TABLE events RENAME COLUMN sequence TO sequence_number;", token).ConfigureAwait(false);
                        }
                        await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO schema_migrations(version,applied_utc) VALUES(3,@now)", token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO tenants(id,name,created_utc,updated_utc) VALUES('ten_local','Local',@now,@now)", token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, "INSERT OR IGNORE INTO users(id,tenant_id,name,role,created_utc,updated_utc) VALUES('usr_local','ten_local','Local owner','admin',@now,@now)", token).ConfigureAwait(false);
                        await ExecuteAsync(connection, transaction, "UPDATE messages SET state='interrupted',updated_utc=@now WHERE state='streaming'; UPDATE runs SET state='paused',outcome='interrupted',revision=revision+1,updated_utc=@now WHERE state IN ('agent_turn','window','queued')", token).ConfigureAwait(false);
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
            if (!context.IsAuthenticated) throw new DomainException(401, "unauthenticated", "Authentication required.");
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM conversations WHERE tenant_id=@tenant AND user_id=@user AND id=@id", P("tenant", context.TenantId, "user", context.UserId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Conversation not found.");
                return ReadConversation(reader);
            }
        }

        /// <summary>Search all owned history using escaped literal substring matching and bounded pagination.</summary>
        public async Task<Page> EnumerateConversationsAsync(RequestContext context, string search = "", bool archived = false, int page = 1, int size = 25, CancellationToken token = default)
        {
            if (page < 1 || size < 1 || size > 100) throw new DomainException(400, "invalid_page", "Page must be positive and page size between 1 and 100.");
            string filter = "tenant_id=@tenant AND user_id=@user AND archived=@archived AND (instr(lower(title),lower(@search))>0 OR id IN (SELECT conversation_id FROM messages WHERE tenant_id=@tenant AND instr(lower(content),lower(@search))>0))";
            Dictionary<string, object?> values = P("tenant", context.TenantId, "user", context.UserId, "archived", archived ? 1 : 0, "search", search, "limit", size, "offset", checked((long)(page - 1) * size));
            Page result = new Page { PageNumber = page, PageSize = size };
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            {
                result.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM conversations WHERE " + filter, token, values).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM conversations WHERE " + filter + " ORDER BY pinned DESC,updated_utc DESC,id DESC LIMIT @limit OFFSET @offset", values))
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
                while (await reader.ReadAsync(token).ConfigureAwait(false)) messages.Add(new Message { Id = S(reader, "id"), ConversationId = conversationId, RunId = N(reader, "run_id"), Sequence = Convert.ToInt64(reader["sequence_number"], CultureInfo.InvariantCulture), Speaker = S(reader, "speaker"), Kind = S(reader, "kind"), Content = S(reader, "content"), RecipientId = N(reader, "recipient_id"), State = S(reader, "state"), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) });
            }
            return messages;
        }

        /// <summary>Append a message transactionally with a durable event and optional deduplication receipt.</summary>
        public async Task<Message> AppendMessageAsync(RequestContext context, Message message, string commandKey = "", CancellationToken token = default)
        {
            RequireWrite(context);
            await ReadConversationAsync(context, message.ConversationId, token).ConfigureAwait(false);
            if (message.Content.Length > 1048576) throw new DomainException(400, "message_too_large", "Message exceeds one MiB.");
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Kind + "\n" + message.RecipientId + "\n" + message.Content)));
            message.Id = Ids.New("msg_"); message.CreatedUtc = DateTime.UtcNow;
            await WriteAsync(async (connection, transaction) =>
            {
                if (commandKey.Length > 0)
                {
                    using (DbCommand command = Command(connection, transaction, "SELECT digest,result_id FROM command_receipts WHERE tenant_id=@tenant AND user_id=@user AND conversation_id=@conversation AND command_key=@key", P("tenant", context.TenantId, "user", context.UserId, "conversation", message.ConversationId, "key", commandKey)))
                    using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            if (S(reader, "digest") != digest) throw new DomainException(409, "idempotency_conflict", "Command key was already used with different content.");
                            message.Id = S(reader, "result_id"); return;
                        }
                    }
                }
                message.Sequence = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COALESCE(MAX(sequence_number),0)+1 FROM messages WHERE conversation_id=@id", token, P("id", message.ConversationId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await ExecuteAsync(connection, transaction, "INSERT INTO messages(id,tenant_id,conversation_id,run_id,sequence_number,speaker,kind,content,recipient_id,state,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@run,@seq,@speaker,@kind,@content,@recipient,@state,@now,@now)", token, P("id", message.Id, "tenant", context.TenantId, "conversation", message.ConversationId, "run", message.RunId, "seq", message.Sequence, "speaker", message.Speaker, "kind", message.Kind, "content", message.Content, "recipient", message.RecipientId, "state", message.State)).ConfigureAwait(false);
                if (commandKey.Length > 0) await ExecuteAsync(connection, transaction, "INSERT INTO command_receipts(id,tenant_id,user_id,conversation_id,command_key,digest,result_id,created_utc,updated_utc) VALUES(@id,@tenant,@user,@conversation,@key,@digest,@result,@now,@now)", token, P("id", Ids.New("cmd_"), "tenant", context.TenantId, "user", context.UserId, "conversation", message.ConversationId, "key", commandKey, "digest", digest, "result", message.Id)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "UPDATE conversations SET updated_utc=@now,revision=revision+1 WHERE id=@id AND tenant_id=@tenant", token, P("id", message.ConversationId, "tenant", context.TenantId)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, message.ConversationId, "message", message.Id, "", token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return (await ReadMessagesAsync(context, message.ConversationId, token).ConfigureAwait(false)).Single(item => item.Id == message.Id);
        }

        /// <summary>Persist a full accumulated streaming snapshot and its durable delta in one transaction.</summary>
        public async Task UpdateMessageAsync(RequestContext context, Message message, string delta, CancellationToken token = default)
        {
            RequireWrite(context);
            await ReadConversationAsync(context, message.ConversationId, token).ConfigureAwait(false);
            await WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "UPDATE messages SET content=@content,state=@state,updated_utc=@now WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@id", token, P("content", message.Content, "state", message.State, "tenant", context.TenantId, "conversation", message.ConversationId, "id", message.Id)).ConfigureAwait(false);
                await EventAsync(connection, transaction, context.TenantId, message.ConversationId, message.State == "streaming" ? "delta" : "message_end", message.Id, delta, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <summary>Read durable ordered events after a cursor, bounded to 1000 per call.</summary>
        public async Task<List<DurableEvent>> ReadEventsAsync(RequestContext context, string conversationId, long after = 0, CancellationToken token = default)
        {
            await ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            List<DurableEvent> events = new List<DurableEvent>();
            using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM events WHERE tenant_id=@tenant AND conversation_id=@id AND sequence_number>@after ORDER BY sequence_number LIMIT 1000", P("tenant", context.TenantId, "id", conversationId, "after", after)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false)) events.Add(new DurableEvent { ConversationId = conversationId, Sequence = Convert.ToInt64(reader["sequence_number"], CultureInfo.InvariantCulture), Kind = S(reader, "kind"), EntityId = S(reader, "entity_id"), Text = S(reader, "text"), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) });
            }
            return events;
        }

        /// <summary>Protect an existing schema before changing it. Server-database migrations require an operator backup.</summary>
        protected virtual Task BeforeUpgradeAsync(long version, CancellationToken token)
        { throw new InvalidOperationException("Back up the existing server database and use the documented migration workflow before upgrading schema " + version + "."); }

        /// <summary>Create a consistent provider-specific backup. SQLite supports an online file backup.</summary>
        public virtual Task BackupAsync(string destination, CancellationToken token = default)
        { throw new NotSupportedException("Use the documented logical/provider-native backup workflow for this database."); }

        /// <summary>Reject unauthenticated or read-only mutation requests.</summary>
        public static void RequireWrite(RequestContext context)
        {
            if (!context.IsAuthenticated) throw new DomainException(401, "unauthenticated", "Authentication required.");
            if (context.Role != "admin" && context.Role != "operator") throw new DomainException(403, "forbidden", "This operation requires write permission.");
        }

        internal async Task<DbConnection> OpenAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            DbConnection connection = DatabaseDriverFactory.CreateConnection(_Settings);
            try { await connection.OpenAsync(token).ConfigureAwait(false); return connection; }
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
                    using (DbConnection connection = await OpenAsync(token).ConfigureAwait(false))
                    using (DbTransaction transaction = connection.BeginTransaction())
                    { await action(connection, transaction).ConfigureAwait(false); transaction.Commit(); outcome = "success"; }
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
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO events(id,tenant_id,conversation_id,sequence_number,kind,entity_id,text,created_utc,updated_utc) SELECT @id,@tenant,@conversation,COALESCE(MAX(sequence_number),0)+1,@kind,@entity,@text,@now,@now FROM events WHERE conversation_id=@conversation", token, P("id", Ids.New("evt_"), "tenant", tenant, "conversation", conversation, "kind", kind, "entity", entity, "text", text)).ConfigureAwait(false);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        { DbParameter parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }

        private static Conversation ReadConversation(DbDataReader reader)
        {
            return new Conversation { Id = S(reader, "id"), TenantId = S(reader, "tenant_id"), UserId = S(reader, "user_id"), Title = S(reader, "title"), Pinned = Convert.ToInt64(reader["pinned"], CultureInfo.InvariantCulture) != 0, Archived = Convert.ToInt64(reader["archived"], CultureInfo.InvariantCulture) != 0, Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
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
        protected virtual void Dispose(bool disposing) { if (_Disposed) return; _Disposed = true; if (disposing) _Writer.Dispose(); }
    }
}
