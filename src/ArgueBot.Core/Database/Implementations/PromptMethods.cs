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
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Prompt library storage, conversation selections and the copies each debate freezes.</summary>
    public sealed class PromptMethods : IPromptMethods
    {
        private const string Visible = "(tenant_id=@tenant OR factory=1)";
        private readonly DatabaseDriverBase _Database;
        internal PromptMethods(DatabaseDriverBase database) { _Database = database; }

        /// <inheritdoc />
        public async Task<List<PromptTemplate>> EnumerateAsync(RequestContext context, string purpose = "", CancellationToken token = default)
        {
            RequireAuthenticated(context);
            if (purpose.Length > 0 && !PromptCatalog.Purposes.Contains(purpose)) throw new DomainException(400, "invalid_purpose", "Choose question, response, synthesis, ratification or compaction.", "purpose");
            List<PromptTemplate> result = new List<PromptTemplate>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM prompt_templates WHERE " + Visible + " AND (@purpose='' OR purpose=@purpose)", P("tenant", context.TenantId, "purpose", purpose)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Read(reader)); }
            return result.OrderBy(item => PromptCatalog.Purposes.ToList().IndexOf(item.Purpose)).ThenByDescending(item => item.Factory).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
        }

        /// <inheritdoc />
        public async Task<PromptTemplate> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                PromptTemplate prompt = await ReadAsync(connection, null, context, id, token).ConfigureAwait(false) ?? throw NotFound();
                await WarnAsync(connection, context, prompt, token).ConfigureAwait(false);
                return prompt;
            }
        }

        /// <summary>Report read_resource references to resources that do not exist in the tenant. These are warnings: the prompt still saves.</summary>
        private static async Task WarnAsync(DbConnection connection, RequestContext context, PromptTemplate prompt, CancellationToken token)
        {
            prompt.Warnings.Clear();
            foreach (string name in PromptCatalog.ResourceReferences(prompt.Body))
            {
                if (await ScalarAsync(connection, null, "SELECT id FROM resources WHERE tenant_id=@tenant AND name_key=@name", token, P("tenant", context.TenantId, "name", name)).ConfigureAwait(false) == null)
                    prompt.Warnings.Add("This prompt refers to the resource " + name + " with read_resource, but no resource has that name. Create it on the Resources page, or fix the name.");
            }
        }

        /// <inheritdoc />
        public async Task<PromptTemplate> CreateAsync(RequestContext context, PromptTemplate prompt, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(prompt);
            PromptCatalog.Validate(prompt);
            string id = Ids.New("pmt_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO prompt_templates(id,tenant_id,name,purpose,description,body,factory,revision,created_utc,updated_utc) VALUES(@id,@tenant,@name,@purpose,@description,@body,0,1,@now,@now)", token, P("id", id, "tenant", context.TenantId, "name", prompt.Name.Trim(), "purpose", prompt.Purpose, "description", prompt.Description.Trim(), "body", prompt.Body)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<PromptTemplate> UpdateAsync(RequestContext context, string id, PromptTemplate prompt, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(prompt);
            PromptTemplate current = await ReadAsync(context, id, token).ConfigureAwait(false);
            if (current.Factory) throw FactoryReadOnly();
            if (prompt.Purpose.Length > 0 && prompt.Purpose != current.Purpose) throw new DomainException(400, "purpose_fixed", "A prompt's purpose cannot change. Duplicate a prompt of the purpose you need instead.", "purpose");
            prompt.Purpose = current.Purpose;
            PromptCatalog.Validate(prompt);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                int changed = await ExecuteAsync(connection, transaction, "UPDATE prompt_templates SET name=@name,description=@description,body=@body,revision=revision+1,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND factory=0 AND revision=@revision", token, P("tenant", context.TenantId, "id", id, "name", prompt.Name.Trim(), "description", prompt.Description.Trim(), "body", prompt.Body, "revision", prompt.Revision)).ConfigureAwait(false);
                if (changed == 0) throw Changed();
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<PromptTemplate> DuplicateAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireWrite(context);
            PromptTemplate source = await ReadAsync(context, id, token).ConfigureAwait(false);
            string name = (source.Factory ? source.Name.Replace(" (default)", "", StringComparison.Ordinal) : source.Name) + " (copy)";
            return await CreateAsync(context, new PromptTemplate { Name = name.Length > 200 ? name.Substring(0, 200) : name, Purpose = source.Purpose, Description = source.Description, Body = source.Body }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(RequestContext context, string id, long revision, CancellationToken token = default)
        {
            RequireWrite(context);
            PromptTemplate current = await ReadAsync(context, id, token).ConfigureAwait(false);
            if (current.Factory) throw new DomainException(409, "factory_prompt", "Factory prompts cannot be deleted. Duplicate one to make an editable copy.");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await ExecuteAsync(connection, transaction, "DELETE FROM prompt_templates WHERE tenant_id=@tenant AND id=@id AND factory=0 AND revision=@revision", token, P("tenant", context.TenantId, "id", id, "revision", revision)).ConfigureAwait(false) == 0) throw Changed();
                await ExecuteAsync(connection, transaction, "DELETE FROM conversation_prompts WHERE tenant_id=@tenant AND prompt_id=@id", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, string>> SelectionAsync(RequestContext context, string conversationId, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return await LoadSelectionAsync(connection, null, context, conversationId, token).ConfigureAwait(false);
        }

        /// <summary>Insert or refresh the factory defaults so they always match this release's built-in text.</summary>
        internal static async Task EnsureFactoryAsync(DbConnection connection, DbTransaction transaction, CancellationToken token)
        {
            foreach (PromptTemplate prompt in PromptCatalog.Factory)
            {
                Dictionary<string, object?> values = P("id", prompt.Id, "name", prompt.Name, "purpose", prompt.Purpose, "description", prompt.Description, "body", prompt.Body);
                object? body = await ScalarAsync(connection, transaction, "SELECT body FROM prompt_templates WHERE id=@id", token, values).ConfigureAwait(false);
                if (body == null) await ExecuteAsync(connection, transaction, "INSERT INTO prompt_templates(id,tenant_id,name,purpose,description,body,factory,revision,created_utc,updated_utc) VALUES(@id,'',@name,@purpose,@description,@body,1,1,@now,@now)", token, values).ConfigureAwait(false);
                else if ((string)body != prompt.Body) await ExecuteAsync(connection, transaction, "UPDATE prompt_templates SET name=@name,description=@description,body=@body,revision=revision+1,updated_utc=@now WHERE id=@id AND factory=1", token, values).ConfigureAwait(false);
                else await ExecuteAsync(connection, transaction, "UPDATE prompt_templates SET name=@name,description=@description WHERE id=@id AND factory=1 AND (name<>@name OR description<>@description)", token, values).ConfigureAwait(false);
            }
        }

        /// <summary>Replace a conversation's prompt choices. Factory choices are stored as no choice. Unknown or mismatched prompts are rejected.</summary>
        internal static async Task SaveSelectionAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string conversationId, Dictionary<string, string> selection, CancellationToken token)
        {
            await ValidateSelectionAsync(connection, transaction, context, selection, token).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "DELETE FROM conversation_prompts WHERE tenant_id=@tenant AND conversation_id=@conversation", token, P("tenant", context.TenantId, "conversation", conversationId)).ConfigureAwait(false);
            foreach (KeyValuePair<string, string> pair in selection)
            {
                if (pair.Value.Length == 0 || pair.Value == PromptCatalog.FactoryId(pair.Key)) continue;
                await ExecuteAsync(connection, transaction, "INSERT INTO conversation_prompts(id,tenant_id,conversation_id,purpose,prompt_id,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@purpose,@prompt,@now,@now)", token, P("id", Ids.New("cpr_"), "tenant", context.TenantId, "conversation", conversationId, "purpose", pair.Key, "prompt", pair.Value)).ConfigureAwait(false);
            }
        }

        /// <summary>Check that each chosen prompt exists, is visible and matches its purpose.</summary>
        internal static async Task ValidateSelectionAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, Dictionary<string, string> selection, CancellationToken token)
        {
            foreach (KeyValuePair<string, string> pair in selection)
            {
                if (!PromptCatalog.Purposes.Contains(pair.Key)) throw new DomainException(400, "invalid_purpose", "Prompt choices use the purposes question, response, synthesis, ratification and compaction.", "promptIds");
                if (pair.Value.Length == 0) continue;
                PromptTemplate? prompt = await ReadAsync(connection, transaction, context, pair.Value, token).ConfigureAwait(false);
                if (prompt == null) throw new DomainException(400, "prompt_not_found", "The " + pair.Key + " prompt no longer exists. Choose another prompt.", "promptIds." + pair.Key);
                if (prompt.Purpose != pair.Key) throw new DomainException(400, "prompt_purpose_mismatch", "A " + prompt.Purpose + " prompt cannot be used for the " + pair.Key + " step.", "promptIds." + pair.Key);
            }
        }

        /// <summary>The prompt chosen for each purpose in a conversation.</summary>
        internal static async Task<Dictionary<string, string>> LoadSelectionAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, string conversationId, CancellationToken token)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            using (DbCommand command = Command(connection, transaction, "SELECT purpose,prompt_id FROM conversation_prompts WHERE tenant_id=@tenant AND conversation_id=@conversation", P("tenant", context.TenantId, "conversation", conversationId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result[S(reader, "purpose")] = S(reader, "prompt_id"); }
            return result;
        }

        /// <summary>Freeze one prompt per purpose for a new debate: the request's choice, else the conversation's, else the factory default.</summary>
        internal static async Task FreezeAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string conversationId, string runId, Dictionary<string, string> overrides, CancellationToken token)
        {
            Dictionary<string, string> chosen = await LoadSelectionAsync(connection, transaction, context, conversationId, token).ConfigureAwait(false);
            foreach (KeyValuePair<string, string> pair in overrides) chosen[pair.Key] = pair.Value;
            foreach (string purpose in PromptCatalog.Purposes)
            {
                string id = chosen.TryGetValue(purpose, out string? value) && value.Length > 0 ? value : PromptCatalog.FactoryId(purpose);
                PromptTemplate prompt = await ReadAsync(connection, transaction, context, id, token).ConfigureAwait(false) ?? PromptCatalog.FactoryFor(purpose);
                await ExecuteAsync(connection, transaction, "INSERT INTO run_prompts(id,tenant_id,run_id,purpose,prompt_id,prompt_revision,name,body,created_utc,updated_utc) VALUES(@id,@tenant,@run,@purpose,@prompt,@revision,@name,@body,@now,@now)", token, P("id", Ids.New("rpr_"), "tenant", context.TenantId, "run", runId, "purpose", purpose, "prompt", prompt.Id, "revision", prompt.Revision, "name", prompt.Name, "body", prompt.Body)).ConfigureAwait(false);
            }
        }

        /// <summary>Attach a debate's frozen prompts.</summary>
        internal static async Task LoadRunAsync(DbConnection connection, RequestContext context, DebateRun run, CancellationToken token)
        {
            run.Prompts.Clear();
            using (DbCommand command = Command(connection, null, "SELECT purpose,prompt_id,prompt_revision,name,body FROM run_prompts WHERE tenant_id=@tenant AND run_id=@run", P("tenant", context.TenantId, "run", run.Id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    run.Prompts.Add(new RunPrompt { Purpose = S(reader, "purpose"), PromptId = S(reader, "prompt_id"), PromptRevision = Convert.ToInt64(reader["prompt_revision"], CultureInfo.InvariantCulture), Name = S(reader, "name"), Body = S(reader, "body") });
            }
            run.Prompts.Sort((left, right) => PromptCatalog.Purposes.ToList().IndexOf(left.Purpose).CompareTo(PromptCatalog.Purposes.ToList().IndexOf(right.Purpose)));
        }

        private static async Task<PromptTemplate?> ReadAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, string id, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM prompt_templates WHERE " + Visible + " AND id=@id", P("tenant", context.TenantId, "id", id)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
        }

        private static PromptTemplate Read(DbDataReader reader)
        {
            return new PromptTemplate
            {
                Id = S(reader, "id"), Name = S(reader, "name"), Purpose = S(reader, "purpose"), Description = S(reader, "description"), Body = S(reader, "body"),
                Factory = Convert.ToInt64(reader["factory"], CultureInfo.InvariantCulture) != 0, Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture),
                CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), UpdatedUtc = DateTime.Parse(S(reader, "updated_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            };
        }
        private static DomainException NotFound() { return new DomainException(404, "prompt_not_found", "Prompt not found."); }
        private static DomainException FactoryReadOnly() { return new DomainException(409, "factory_prompt", "Factory prompts are read-only. Duplicate this prompt to make an editable copy."); }
        private static DomainException Changed() { return new DomainException(409, "revision_conflict", "This prompt changed or was deleted. Reload it before saving."); }
    }
}
