namespace ArgueBot.Core.Database.Implementations
{
    using ArgueBot.Core.Database.Interfaces;
    using ArgueBot.Core.Models;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using static ArgueBot.Core.Database.DatabaseDriverBase;

    /// <summary>Moderator-facing read model for durable information requests.</summary>
    public sealed class ClarificationMethods : IClarificationMethods
    {
        private readonly DatabaseDriverBase _Database;
        internal ClarificationMethods(DatabaseDriverBase database) { _Database = database; }
        /// <summary>Check all unresolved blocking requests without depending on a display page.</summary>
        public async Task<bool> HasBlockingAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default)
        {
            await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT id FROM debate_clarifications WHERE tenant_id=@tenant AND run_id=@run AND blocking=1 AND status='open' LIMIT 1", P("tenant", context.TenantId, "run", runId)))
            { return await command.ExecuteScalarAsync(token).ConfigureAwait(false) != null; }
        }
        /// <summary>Read a bounded page of one hundred requests, including answered requests.</summary>
        public async Task<List<ClarificationRequest>> EnumerateAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default, int pageNumber = 1)
        {
            CatalogMethods.ValidatePage(pageNumber, 100);
            await _Database.Runs.ReadAsync(context, conversationId, runId, token).ConfigureAwait(false);
            List<ClarificationRequest> result = new List<ClarificationRequest>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM debate_clarifications WHERE tenant_id=@tenant AND run_id=@run ORDER BY created_utc DESC,id LIMIT 100 OFFSET @offset", P("tenant", context.TenantId, "run", runId, "offset", ((long)pageNumber - 1) * 100)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(new ClarificationRequest { Id = S(reader, "id"), ParticipantId = S(reader, "profile_id"), Question = S(reader, "question"), Blocking = System.Convert.ToInt32(reader["blocking"], CultureInfo.InvariantCulture) != 0, Status = S(reader, "status") });
            }
            return result;
        }
    }
}
