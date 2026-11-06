namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Resource library storage. Every change writes a full revision row in the same transaction and prunes history beyond the kept limit.</summary>
    public sealed class ResourceMethods : IResourceMethods
    {
        private const string SummaryColumns = "id,name,title,description,tags_json,characters,bytes,locked,revision,created_by_user_id,updated_by_kind,updated_by_id,updated_by_name,created_utc,updated_utc";
        private readonly DatabaseDriverBase _Database;
        internal ResourceMethods(DatabaseDriverBase database) { _Database = database; }

        /// <inheritdoc />
        public async Task<ResourcePage> EnumerateAsync(RequestContext context, string search = "", string tag = "", int pageNumber = 1, int pageSize = 50, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            ArgumentNullException.ThrowIfNull(search); ArgumentNullException.ThrowIfNull(tag);
            if (pageNumber < 1) throw new DomainException(400, "invalid_page", "Use a page number of 1 or more.", "pageNumber");
            if (pageSize < 1 || pageSize > 200) throw new DomainException(400, "invalid_page", "Use a page size from 1 through 200.", "pageSize");
            if (search.Length > 4000) throw new DomainException(400, "invalid_search", "Search text must fit 4000 characters.", "search");
            string tagKey = tag.Trim().ToLowerInvariant();
            List<ResourceSummary> matches = (await SummariesAsync(context, token).ConfigureAwait(false))
                .Where(item => tagKey.Length == 0 || item.Tags.Contains(tagKey, StringComparer.Ordinal))
                .Where(item => search.Length == 0 || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new ResourcePage { Items = matches.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), PageNumber = pageNumber, PageSize = pageSize, TotalCount = matches.Count };
        }

        /// <inheritdoc />
        public async Task<List<ResourceSummary>> SummariesAsync(RequestContext context, CancellationToken token = default)
        {
            RequireAuthenticated(context);
            List<ResourceSummary> result = new List<ResourceSummary>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT " + SummaryColumns + " FROM resources WHERE tenant_id=@tenant ORDER BY name_key", P("tenant", context.TenantId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Summary(reader)); }
            return result.OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
        }

        /// <inheritdoc />
        public async Task<Resource> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(id);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return await LoadAsync(connection, null, context, "id=@key", id, token).ConfigureAwait(false) ?? throw NotFound();
        }

        /// <inheritdoc />
        public async Task<Resource> ReadByNameAsync(RequestContext context, string name, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(name);
            if (!ResourceRules.TryNormalizeName(name, out string key)) throw NotFound();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return await LoadAsync(connection, null, context, "name_key=@key", key, token).ConfigureAwait(false) ?? throw NotFound();
        }

        /// <inheritdoc />
        public async Task<Resource> CreateAsync(RequestContext context, ResourceRequest request, ResourceAuthor? author = null, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(request);
            if (author != null && request.Locked == true) throw new DomainException(403, "resource_lock_denied", "Agents cannot create locked resources.", "locked");
            string id = Ids.New("res_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await InsertAsync(connection, transaction, context, id, request, author, "create", token).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "ResourceCreated", id, author, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Resource> UpdateAsync(RequestContext context, string id, ResourceRequest request, ResourceAuthor? author = null, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(request);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Resource current = await LoadAsync(connection, transaction, context, "id=@key", id, token).ConfigureAwait(false) ?? throw NotFound();
                if (request.Revision != current.Revision) throw Conflict(current.Revision);
                if (author != null)
                {
                    if (current.Locked) throw Locked(current.Name);
                    if (request.Locked.HasValue && request.Locked.Value != current.Locked) throw new DomainException(403, "resource_lock_denied", "Agents cannot lock or unlock resources.", "locked");
                    if (request.Name != null && ResourceRules.NormalizeName(request.Name) != current.Name) throw new DomainException(403, "resource_rename_denied", "Agents cannot rename resources.", "name");
                }
                string name = request.Name == null ? current.Name : ResourceRules.NormalizeName(request.Name);
                string change = name != current.Name ? "rename" : request.Locked.HasValue && request.Locked.Value != current.Locked ? (request.Locked.Value ? "lock" : "unlock") : "update";
                Resource next = Apply(current, request, name);
                await SaveAsync(connection, transaction, context, current, next, author, change, token).ConfigureAwait(false);
                string operation = change == "rename" ? "ResourceRenamed" : change == "lock" ? "ResourceLocked" : change == "unlock" ? "ResourceUnlocked" : "ResourceUpdated";
                await AuditAsync(connection, transaction, context, operation, id, author, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Resource> AppendAsync(RequestContext context, string id, string text, ResourceAuthor? author = null, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(text);
            if (text.Length == 0) throw new DomainException(400, "missing_text", "Provide text to append.", "text");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Resource current = await LoadAsync(connection, transaction, context, "id=@key", id, token).ConfigureAwait(false) ?? throw NotFound();
                if (author != null && current.Locked) throw Locked(current.Name);
                string separator = current.Body.Length == 0 || current.Body.EndsWith('\n') ? "" : "\n";
                Resource next = Apply(current, new ResourceRequest { Body = current.Body + separator + text }, current.Name);
                await SaveAsync(connection, transaction, context, current, next, author, "append", token).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "ResourceUpdated", id, author, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(RequestContext context, string id, long revision, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(id);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Resource current = await LoadAsync(connection, transaction, context, "id=@key", id, token).ConfigureAwait(false) ?? throw NotFound();
                if (revision != current.Revision) throw Conflict(current.Revision);
                await ExecuteAsync(connection, transaction, "DELETE FROM resource_revisions WHERE tenant_id=@tenant AND resource_id=@id", token, P("tenant", context.TenantId, "id", id)).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction, "DELETE FROM resources WHERE tenant_id=@tenant AND id=@id AND revision=@revision", token, P("tenant", context.TenantId, "id", id, "revision", revision)).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "ResourceDeleted", id, null, token, current.Name).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ResourceRevision>> RevisionsAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(id);
            List<ResourceRevision> result = new List<ResourceRevision>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                if (await LoadAsync(connection, null, context, "id=@key", id, token).ConfigureAwait(false) == null) throw NotFound();
                using (DbCommand command = Command(connection, null, "SELECT id,resource_id,revision,name,title,description,tags_json,characters,locked,change_kind,author_kind,author_id,author_name,run_id,attempt_id,created_utc FROM resource_revisions WHERE tenant_id=@tenant AND resource_id=@id ORDER BY revision DESC", P("tenant", context.TenantId, "id", id)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Revision(reader, false)); }
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<ResourceRevision> ReadRevisionAsync(RequestContext context, string id, long revision, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(id);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                return await LoadRevisionAsync(connection, null, context, id, revision, token).ConfigureAwait(false) ?? throw new DomainException(404, "revision_not_found", "That revision is not in the saved history. The service keeps the latest " + ResourceRules.MaxRevisionsKept + " revisions.");
        }

        /// <inheritdoc />
        public async Task<Resource> RevertAsync(RequestContext context, string id, ResourceRevertRequest request, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(id); ArgumentNullException.ThrowIfNull(request);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                Resource current = await LoadAsync(connection, transaction, context, "id=@key", id, token).ConfigureAwait(false) ?? throw NotFound();
                if (request.Revision != current.Revision) throw Conflict(current.Revision);
                ResourceRevision target = await LoadRevisionAsync(connection, transaction, context, id, request.TargetRevision, token).ConfigureAwait(false) ?? throw new DomainException(404, "revision_not_found", "That revision is not in the saved history.", "targetRevision");
                Resource next = Apply(current, new ResourceRequest { Title = target.Title, Description = target.Description, Tags = target.Tags, Body = target.Body }, current.Name);
                await SaveAsync(connection, transaction, context, current, next, null, "revert", token).ConfigureAwait(false);
                await AuditAsync(connection, transaction, context, "ResourceReverted", id, null, token, "Restored revision " + request.TargetRevision.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return await ReadAsync(context, id, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<ResourceImportResult> ImportAsync(RequestContext context, ResourceImportRequest request, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(request);
            if (request.Files.Count < 1 || request.Files.Count > ResourceRules.MaxImportFiles) throw new DomainException(400, "invalid_import", "Import 1 to " + ResourceRules.MaxImportFiles + " files at once.", "files");
            List<ResourceRequest> parsed = new List<ResourceRequest>();
            for (int index = 0; index < request.Files.Count; index++)
            {
                ResourceRequest item = ResourceMarkdown.Parse(request.Files[index].FileName, request.Files[index].Content);
                if (!ResourceRules.TryNormalizeName(item.Name, out string name)) throw new DomainException(400, "invalid_resource_name", "File " + (index + 1) + " (" + request.Files[index].FileName + ") needs a valid name in its front matter or file name.", "files[" + index + "].name");
                if (parsed.Any(other => other.Name == name)) throw new DomainException(400, "duplicate_resource_name", "Two files in this import use the name " + name + ".", "files[" + index + "].name");
                item.Name = name; parsed.Add(item);
            }
            ResourceImportResult result = new ResourceImportResult();
            List<string> ids = new List<string>();
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                foreach (ResourceRequest item in parsed)
                {
                    Resource? existing = await LoadAsync(connection, transaction, context, "name_key=@key", item.Name!, token).ConfigureAwait(false);
                    if (existing == null)
                    {
                        string id = Ids.New("res_");
                        await InsertAsync(connection, transaction, context, id, item, null, "import", token).ConfigureAwait(false);
                        ids.Add(id); result.Created++;
                    }
                    else
                    {
                        if (!request.Overwrite) throw new DomainException(409, "resource_exists", "A resource named " + existing.Name + " already exists. Import with overwrite to replace it.", "files");
                        Resource next = Apply(existing, new ResourceRequest { Title = item.Title, Description = item.Description, Tags = item.Tags, Body = item.Body, Locked = item.Locked }, existing.Name);
                        await SaveAsync(connection, transaction, context, existing, next, null, "import", token).ConfigureAwait(false);
                        ids.Add(existing.Id); result.Updated++;
                    }
                }
                await AuditAsync(connection, transaction, context, "ResourceImported", string.Join(",", ids), null, token, result.Created + " created, " + result.Updated + " replaced").ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            List<ResourceSummary> summaries = await SummariesAsync(context, token).ConfigureAwait(false);
            foreach (string id in ids) { ResourceSummary? summary = summaries.FirstOrDefault(item => item.Id == id); if (summary != null) result.Items.Add(summary); }
            return result;
        }

        /// <inheritdoc />
        public async Task<List<ResourceSearchHit>> SearchAsync(RequestContext context, string query, ICollection<string>? visibleIds = null, int limit = 20, CancellationToken token = default)
        {
            RequireAuthenticated(context); ArgumentNullException.ThrowIfNull(query);
            if (query.Trim().Length == 0 || query.Length > 4000) throw new DomainException(400, "missing_query", "Provide 1 to 4000 characters to search for.", "query");
            if (limit < 1 || limit > 50) throw new DomainException(400, "invalid_limit", "Use a limit from 1 through 50.", "limit");
            List<ResourceSearchHit> result = new List<ResourceSearchHit>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT id,name,title,description,body,revision FROM resources WHERE tenant_id=@tenant ORDER BY name_key", P("tenant", context.TenantId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (result.Count < limit && await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    string id = S(reader, "id");
                    if (visibleIds != null && !visibleIds.Contains(id)) continue;
                    ResourceSearchHit hit = new ResourceSearchHit { Id = id, Name = S(reader, "name"), Title = S(reader, "title"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture) };
                    string description = S(reader, "description"); string body = S(reader, "body");
                    int match = body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (match >= 0) { hit.Field = "body"; hit.MatchOffset = match; hit.Snippet = Snippet(body, match, query.Length); }
                    else if (hit.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) { hit.Field = "name"; hit.Snippet = description; }
                    else if (hit.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) { hit.Field = "title"; hit.Snippet = description; }
                    else if (description.Contains(query, StringComparison.OrdinalIgnoreCase)) { hit.Field = "description"; hit.Snippet = description; }
                    else continue;
                    result.Add(hit);
                }
            }
            return result;
        }

        private static Resource Apply(Resource current, ResourceRequest request, string name)
        {
            string body = request.Body == null ? current.Body : ResourceRules.Body(request.Body);
            return new Resource
            {
                Id = current.Id, Name = name,
                Title = request.Title == null ? current.Title : ResourceRules.Title(request.Title),
                Description = request.Description == null ? current.Description : ResourceRules.Description(request.Description),
                Tags = request.Tags == null ? current.Tags : ResourceRules.NormalizeTags(request.Tags),
                Body = body, Characters = body.Length, Bytes = Encoding.UTF8.GetByteCount(body),
                Locked = request.Locked ?? current.Locked, Revision = current.Revision + 1, CreatedByUserId = current.CreatedByUserId
            };
        }

        private static async Task InsertAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string id, ResourceRequest request, ResourceAuthor? author, string change, CancellationToken token)
        {
            string name = ResourceRules.NormalizeName(request.Name);
            string title = ResourceRules.Title(request.Title); string description = ResourceRules.Description(request.Description);
            List<string> tags = ResourceRules.NormalizeTags(request.Tags); string body = ResourceRules.Body(request.Body);
            long count = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM resources WHERE tenant_id=@tenant", token, P("tenant", context.TenantId)).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (count >= ResourceRules.MaxResourcesPerTenant) throw new DomainException(409, "resource_limit", "This tenant already has " + ResourceRules.MaxResourcesPerTenant + " resources. Delete some before adding more.");
            if (await ScalarAsync(connection, transaction, "SELECT id FROM resources WHERE tenant_id=@tenant AND name_key=@name", token, P("tenant", context.TenantId, "name", name)).ConfigureAwait(false) != null) throw Exists(name);
            string authorName = await AuthorNameAsync(connection, transaction, context, author, token).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "INSERT INTO resources(id,tenant_id,name,name_key,title,description,tags_json,body,characters,bytes,locked,revision,created_by_user_id,updated_by_kind,updated_by_id,updated_by_name,created_utc,updated_utc) VALUES(@id,@tenant,@name,@name,@title,@description,@tags,@body,@characters,@bytes,@locked,1,@user,@kind,@author,@authorName,@now,@now)", token,
                P("id", id, "tenant", context.TenantId, "name", name, "title", title, "description", description, "tags", JsonSerializer.Serialize(tags), "body", body, "characters", (long)body.Length, "bytes", (long)Encoding.UTF8.GetByteCount(body), "locked", request.Locked == true ? 1 : 0, "user", context.UserId, "kind", author == null ? "user" : "agent", "author", author == null ? context.UserId : author.SeatId, "authorName", authorName)).ConfigureAwait(false);
            Resource created = new Resource { Id = id, Name = name, Title = title, Description = description, Tags = tags, Body = body, Characters = body.Length, Locked = request.Locked == true, Revision = 1 };
            await RevisionAsync(connection, transaction, context, created, author, authorName, change, token).ConfigureAwait(false);
        }

        private static async Task SaveAsync(DbConnection connection, DbTransaction transaction, RequestContext context, Resource current, Resource next, ResourceAuthor? author, string change, CancellationToken token)
        {
            if (next.Name != current.Name && await ScalarAsync(connection, transaction, "SELECT id FROM resources WHERE tenant_id=@tenant AND name_key=@name AND id<>@id", token, P("tenant", context.TenantId, "name", next.Name, "id", current.Id)).ConfigureAwait(false) != null) throw Exists(next.Name);
            string authorName = await AuthorNameAsync(connection, transaction, context, author, token).ConfigureAwait(false);
            int changed = await ExecuteAsync(connection, transaction, "UPDATE resources SET name=@name,name_key=@name,title=@title,description=@description,tags_json=@tags,body=@body,characters=@characters,bytes=@bytes,locked=@locked,revision=@next,updated_by_kind=@kind,updated_by_id=@author,updated_by_name=@authorName,updated_utc=@now WHERE tenant_id=@tenant AND id=@id AND revision=@revision", token,
                P("tenant", context.TenantId, "id", current.Id, "revision", current.Revision, "next", next.Revision, "name", next.Name, "title", next.Title, "description", next.Description, "tags", JsonSerializer.Serialize(next.Tags), "body", next.Body, "characters", next.Characters, "bytes", next.Bytes, "locked", next.Locked ? 1 : 0, "kind", author == null ? "user" : "agent", "author", author == null ? context.UserId : author.SeatId, "authorName", authorName)).ConfigureAwait(false);
            if (changed != 1) throw Conflict(current.Revision);
            await RevisionAsync(connection, transaction, context, next, author, authorName, change, token).ConfigureAwait(false);
        }

        private static async Task RevisionAsync(DbConnection connection, DbTransaction transaction, RequestContext context, Resource resource, ResourceAuthor? author, string authorName, string change, CancellationToken token)
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO resource_revisions(id,tenant_id,resource_id,revision,name,title,description,tags_json,body,characters,locked,change_kind,author_kind,author_id,author_name,run_id,attempt_id,created_utc,updated_utc) VALUES(@id,@tenant,@resource,@revision,@name,@title,@description,@tags,@body,@characters,@locked,@change,@kind,@author,@authorName,@run,@attempt,@now,@now)", token,
                P("id", Ids.New("rsr_"), "tenant", context.TenantId, "resource", resource.Id, "revision", resource.Revision, "name", resource.Name, "title", resource.Title, "description", resource.Description, "tags", JsonSerializer.Serialize(resource.Tags), "body", resource.Body, "characters", (long)resource.Body.Length, "locked", resource.Locked ? 1 : 0, "change", change, "kind", author == null ? "user" : "agent", "author", author == null ? context.UserId : author.SeatId, "authorName", authorName, "run", author?.RunId ?? "", "attempt", author?.AttemptId ?? "")).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction, "DELETE FROM resource_revisions WHERE tenant_id=@tenant AND resource_id=@resource AND revision<=@oldest", token, P("tenant", context.TenantId, "resource", resource.Id, "oldest", resource.Revision - ResourceRules.MaxRevisionsKept)).ConfigureAwait(false);
        }

        private static async Task<string> AuthorNameAsync(DbConnection connection, DbTransaction transaction, RequestContext context, ResourceAuthor? author, CancellationToken token)
        {
            if (author != null) return author.Name;
            using (DbCommand command = Command(connection, transaction, "SELECT name,first_name,last_name,email FROM users WHERE tenant_id=@tenant AND id=@user", P("tenant", context.TenantId, "user", context.UserId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return context.UserId;
                string full = (S(reader, "first_name") + " " + S(reader, "last_name")).Trim();
                return full.Length > 0 ? full : S(reader, "name").Length > 0 ? S(reader, "name") : S(reader, "email");
            }
        }

        private static Task AuditAsync(DbConnection connection, DbTransaction transaction, RequestContext context, string operation, string id, ResourceAuthor? author, CancellationToken token, string reason = "")
        {
            string detail = author == null ? reason : ("Agent " + author.Name + " (" + author.SeatId + ") in run " + author.RunId + (reason.Length > 0 ? ". " + reason : ""));
            return AuditLog.WriteAsync(connection, transaction, new AuditEvent { TenantId = context.TenantId, UserId = context.UserId, Operation = operation, Outcome = "success", ResourceType = "resource", ResourceId = id, Reason = detail, PrincipalType = context.PrincipalType, SessionId = context.SessionId, CredentialId = context.CredentialId, AuthScheme = context.AuthScheme, CorrelationId = context.CorrelationId, SourceIp = context.SourceIp }, token);
        }

        private static async Task<Resource?> LoadAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, string where, string key, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT " + SummaryColumns + ",body FROM resources WHERE tenant_id=@tenant AND " + where, P("tenant", context.TenantId, "key", key)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
                ResourceSummary summary = Summary(reader);
                return new Resource
                {
                    Id = summary.Id, Name = summary.Name, Title = summary.Title, Description = summary.Description, Tags = summary.Tags, Body = S(reader, "body"), Characters = summary.Characters, Bytes = summary.Bytes, Locked = summary.Locked, Revision = summary.Revision,
                    CreatedByUserId = summary.CreatedByUserId, UpdatedByKind = summary.UpdatedByKind, UpdatedById = summary.UpdatedById, UpdatedByName = summary.UpdatedByName, CreatedUtc = summary.CreatedUtc, UpdatedUtc = summary.UpdatedUtc
                };
            }
        }

        private static async Task<ResourceRevision?> LoadRevisionAsync(DbConnection connection, DbTransaction? transaction, RequestContext context, string id, long revision, CancellationToken token)
        {
            using (DbCommand command = Command(connection, transaction, "SELECT * FROM resource_revisions WHERE tenant_id=@tenant AND resource_id=@id AND revision=@revision", P("tenant", context.TenantId, "id", id, "revision", revision)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                return await reader.ReadAsync(token).ConfigureAwait(false) ? Revision(reader, true) : null;
        }

        private static ResourceSummary Summary(DbDataReader reader)
        {
            return new ResourceSummary
            {
                Id = S(reader, "id"), Name = S(reader, "name"), Title = S(reader, "title"), Description = S(reader, "description"), Tags = Tags(S(reader, "tags_json")),
                Characters = Convert.ToInt64(reader["characters"], CultureInfo.InvariantCulture), Bytes = Convert.ToInt64(reader["bytes"], CultureInfo.InvariantCulture),
                Locked = Convert.ToInt64(reader["locked"], CultureInfo.InvariantCulture) != 0, Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture),
                CreatedByUserId = S(reader, "created_by_user_id"), UpdatedByKind = S(reader, "updated_by_kind"), UpdatedById = S(reader, "updated_by_id"), UpdatedByName = S(reader, "updated_by_name"),
                CreatedUtc = Time(S(reader, "created_utc")), UpdatedUtc = Time(S(reader, "updated_utc"))
            };
        }

        private static ResourceRevision Revision(DbDataReader reader, bool body)
        {
            return new ResourceRevision
            {
                Id = S(reader, "id"), ResourceId = S(reader, "resource_id"), Revision = Convert.ToInt64(reader["revision"], CultureInfo.InvariantCulture), Name = S(reader, "name"), Title = S(reader, "title"), Description = S(reader, "description"),
                Tags = Tags(S(reader, "tags_json")), Body = body ? S(reader, "body") : "", Characters = Convert.ToInt64(reader["characters"], CultureInfo.InvariantCulture), Locked = Convert.ToInt64(reader["locked"], CultureInfo.InvariantCulture) != 0,
                Change = S(reader, "change_kind"), AuthorKind = S(reader, "author_kind"), AuthorId = S(reader, "author_id"), AuthorName = S(reader, "author_name"), RunId = S(reader, "run_id"), AttemptId = S(reader, "attempt_id"), CreatedUtc = Time(S(reader, "created_utc"))
            };
        }

        private static List<string> Tags(string json)
        {
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch (JsonException) { return new List<string>(); }
        }

        private static DateTime Time(string value) { return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }

        private static string Snippet(string content, int match, int length)
        {
            int start = Math.Max(0, match - 200);
            int end = Math.Min(content.Length, match + length + 200);
            if (start > 0 && char.IsLowSurrogate(content[start])) start--;
            if (end < content.Length && end > 0 && char.IsHighSurrogate(content[end - 1])) end++;
            return (start > 0 ? "..." : "") + content.Substring(start, end - start) + (end < content.Length ? "..." : "");
        }

        private static DomainException NotFound() { return new DomainException(404, "resource_not_found", "Resource not found."); }
        private static DomainException Exists(string name) { return new DomainException(409, "resource_exists", "A resource named " + name + " already exists. Choose another name.", "name"); }
        private static DomainException Locked(string name) { return new DomainException(409, "resource_locked", "The resource " + name + " is locked. Agents can read it but not change it."); }
        private static DomainException Conflict(long current) { return new DomainException(409, "revision_conflict", "This resource changed since you read it. Its current revision is " + current.ToString(CultureInfo.InvariantCulture) + ". Read it again and reapply your change."); }
    }
}
