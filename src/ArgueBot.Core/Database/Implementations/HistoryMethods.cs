namespace ArgueBot.Core.Database.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Helpers;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Structured request capture, authorized inspection, summaries and retention.</summary>
    public sealed class HistoryMethods : IHistoryMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal HistoryMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Persist redacted diagnostics. The caller must apply the capture policy before invoking this method.</summary>
        public async Task InsertAsync(RequestHistoryEntry entry, CancellationToken token = default)
        {
            entry.Id = Ids.New("req_");
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO request_history(id,tenant_id,user_id,principal_name,method,path,status,duration_ms,request_bytes,response_bytes,raw_request_body,raw_response_body,request_truncated,response_truncated,correlation_id,trace_id,created_utc,updated_utc) VALUES(@id,@tenant,@user,@principal,@method,@path,@status,@duration,@requestBytes,@responseBytes,@requestBody,@responseBody,@requestTruncated,@responseTruncated,@correlation,@trace,@created,@now)", token, P("id", entry.Id, "tenant", entry.TenantId, "user", entry.UserId, "principal", entry.PrincipalName, "method", entry.Method, "path", entry.Path, "status", entry.Status, "duration", entry.DurationMs, "requestBytes", entry.RequestBytes, "responseBytes", entry.ResponseBytes, "requestBody", entry.RawRequestBody, "responseBody", entry.RawResponseBody, "requestTruncated", entry.RequestTruncated ? 1 : 0, "responseTruncated", entry.ResponseTruncated ? 1 : 0, "correlation", entry.CorrelationId, "trace", entry.TraceId, "created", entry.CreatedUtc.ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false);
                foreach (CapturedHeader header in entry.Headers)
                {
                    await ExecuteAsync(connection, transaction, "INSERT INTO request_history_headers(id,tenant_id,history_id,name,value,response,created_utc,updated_utc) VALUES(@id,@tenant,@history,@name,@value,@response,@now,@now)", token, P("id", Ids.New("hdr_"), "tenant", entry.TenantId, "history", entry.Id, "name", header.Name, "value", header.Value, "response", header.Response ? 1 : 0)).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
        }
        /// <summary>Enumerate metadata with backend filtering and exact totals. Body/header data is loaded only by detail.</summary>
        public async Task<HistoryPage> EnumerateAsync(RequestContext context, HistoryFilter filter, CancellationToken token = default)
        {
            Authorize(context);
            if (filter.PageNumber < 1 || filter.PageSize < 1 || filter.PageSize > 100) throw new DomainException(400, "invalid_page", "Request history page size must be 1 to 100.");
            Dictionary<string, object?> values = Values(context, filter);
            values["limit"] = filter.PageSize; values["offset"] = checked((long)(filter.PageNumber - 1) * filter.PageSize);
            string where = Scope(context) + " AND (@method='' OR method=@method) AND (@status=0 OR status=@status) AND instr(lower(path),lower(@search))>0 AND (@fromUtc='' OR created_utc>=@fromUtc) AND (@toUtc='' OR created_utc<=@toUtc)";
            HistoryPage page = new HistoryPage { PageNumber = filter.PageNumber, PageSize = filter.PageSize };
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                page.TotalCount = Convert.ToInt64(await ScalarAsync(connection, null, "SELECT COUNT(*) FROM request_history WHERE " + where, token, values).ConfigureAwait(false), CultureInfo.InvariantCulture);
                using (DbCommand command = Command(connection, null, "SELECT * FROM request_history WHERE " + where + " ORDER BY created_utc DESC,id DESC LIMIT @limit OFFSET @offset", values))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) page.Items.Add(Read(reader, false)); }
            }
            return page;
        }
        /// <summary>Read an authorized diagnostic detail including redacted body prefixes and headers.</summary>
        public async Task<RequestHistoryEntry> ReadAsync(RequestContext context, string id, CancellationToken token = default)
        {
            Authorize(context);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            {
                RequestHistoryEntry entry;
                using (DbCommand command = Command(connection, null, "SELECT * FROM request_history WHERE id=@id AND " + Scope(context), P("id", id, "tenant", context.TenantId, "user", context.UserId)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new DomainException(404, "not_found", "Request capture not found."); entry = Read(reader, true); }
                using (DbCommand command = Command(connection, null, "SELECT name,value,response FROM request_history_headers WHERE history_id=@id ORDER BY response,name", P("id", id)))
                using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                { while (await reader.ReadAsync(token).ConfigureAwait(false)) entry.Headers.Add(new CapturedHeader { Name = reader.GetString(0), Value = reader.GetString(1), Response = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != 0 }); }
                return entry;
            }
        }
        /// <summary>Delete one authorized request capture.</summary>
        public async Task DeleteAsync(RequestContext context, string id, CancellationToken token = default)
        {
            RequireWrite(context); await ReadAsync(context, id, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) => { await ExecuteAsync(connection, transaction, "DELETE FROM request_history WHERE id=@id AND " + Scope(context), token, P("id", id, "tenant", context.TenantId, "user", context.UserId)).ConfigureAwait(false); }, token).ConfigureAwait(false);
        }
        /// <summary>Prune records before a UTC cutoff. Only a service maintenance task should call this.</summary>
        public Task PruneAsync(DateTime cutoffUtc, CancellationToken token = default)
        { return _Database.WriteAsync(async (connection, transaction) => { await ExecuteAsync(connection, transaction, "DELETE FROM request_history WHERE created_utc<@cutoff", token, P("cutoff", cutoffUtc.ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false); }, token); }
        /// <summary>Return every time bucket, including empty buckets, for a bounded UTC range.</summary>
        public async Task<List<ActivityBucket>> SummaryAsync(RequestContext context, DateTime fromUtc, DateTime toUtc, int bucketMinutes, CancellationToken token = default)
        {
            Authorize(context);
            if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(31) || bucketMinutes < 1 || bucketMinutes > 1440 || (toUtc - fromUtc).TotalMinutes / bucketMinutes > 2000) throw new DomainException(400, "invalid_range", "Use a positive range up to 31 days and at most 2000 buckets.");
            List<ActivityBucket> buckets = new List<ActivityBucket>();
            for (DateTime time = fromUtc; time < toUtc; time = time.AddMinutes(bucketMinutes)) buckets.Add(new ActivityBucket { StartUtc = time });
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT created_utc,status FROM request_history WHERE " + Scope(context) + " AND created_utc>=@fromUtc AND created_utc<@toUtc", P("tenant", context.TenantId, "user", context.UserId, "fromUtc", fromUtc.ToString("O", CultureInfo.InvariantCulture), "toUtc", toUtc.ToString("O", CultureInfo.InvariantCulture))))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    DateTime created = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    int index = (int)((created - fromUtc).TotalMinutes / bucketMinutes);
                    if (index >= 0 && index < buckets.Count) { if (Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) >= 400) buckets[index].Failure++; else buckets[index].Success++; }
                }
            }
            return buckets;
        }
        private static Dictionary<string, object?> Values(RequestContext context, HistoryFilter filter)
        { return P("tenant", context.TenantId, "user", context.UserId, "method", filter.Method, "status", filter.Status ?? 0, "search", filter.Search, "fromUtc", filter.FromUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "", "toUtc", filter.ToUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? ""); }
        private static string Scope(RequestContext context) { return context.IsAdmin ? "(tenant_id=@tenant OR tenant_id IS NULL)" : "tenant_id=@tenant AND user_id=@user"; }
        private static void Authorize(RequestContext context) { if (!context.IsAuthenticated || (context.Role != "admin" && context.Role != "operator")) throw new DomainException(403, "forbidden", "Request inspection requires operator permission."); }
        private static RequestHistoryEntry Read(DbDataReader reader, bool detail)
        {
            return new RequestHistoryEntry { Id = S(reader, "id"), TenantId = N(reader, "tenant_id"), UserId = N(reader, "user_id"), PrincipalName = S(reader, "principal_name"), Method = S(reader, "method"), Path = S(reader, "path"), Status = Convert.ToInt32(reader["status"], CultureInfo.InvariantCulture), DurationMs = Convert.ToInt64(reader["duration_ms"], CultureInfo.InvariantCulture), RequestBytes = Convert.ToInt64(reader["request_bytes"], CultureInfo.InvariantCulture), ResponseBytes = Convert.ToInt64(reader["response_bytes"], CultureInfo.InvariantCulture), RawRequestBody = detail ? S(reader, "raw_request_body") : "", RawResponseBody = detail ? S(reader, "raw_response_body") : "", RequestTruncated = Convert.ToInt32(reader["request_truncated"], CultureInfo.InvariantCulture) != 0, ResponseTruncated = Convert.ToInt32(reader["response_truncated"], CultureInfo.InvariantCulture) != 0, CorrelationId = S(reader, "correlation_id"), TraceId = S(reader, "trace_id"), CreatedUtc = DateTime.Parse(S(reader, "created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) };
        }
    }
}
