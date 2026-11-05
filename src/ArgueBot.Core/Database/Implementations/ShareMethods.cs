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

    /// <summary>Public conversation links: 32 random bytes per token, stored only as SHA-256, readable until expiry or revocation.</summary>
    public sealed class ShareMethods : IShareMethods
    {
        /// <summary>Longest link lifetime: 90 days.</summary>
        public const int MaximumHours = 2160;
        private readonly DatabaseDriverBase _Database;
        internal ShareMethods(DatabaseDriverBase database) { _Database = database; }

        /// <inheritdoc />
        public async Task<List<ConversationShare>> ListAsync(RequestContext context, string conversationId, CancellationToken token = default)
        {
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            List<ConversationShare> result = new List<ConversationShare>();
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT * FROM conversation_shares WHERE tenant_id=@tenant AND conversation_id=@conversation ORDER BY created_utc DESC,id", P("tenant", context.TenantId, "conversation", conversationId)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            { while (await reader.ReadAsync(token).ConfigureAwait(false)) result.Add(Read(reader)); }
            return result;
        }

        /// <inheritdoc />
        public async Task<ConversationShareCreated> CreateAsync(RequestContext context, string conversationId, ConversationShareRequest request, string baseUrl, CancellationToken token = default)
        {
            RequireWrite(context); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(baseUrl);
            if (request.ExpiresInHours < 1 || request.ExpiresInHours > MaximumHours) throw new DomainException(400, "invalid_expiry", "Choose a lifetime from 1 hour to 90 days.", "expiresInHours");
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            string secret = SecretTokens.Create("", 32);
            string id = Ids.New("shr_");
            DateTime expires = DateTime.UtcNow.AddHours(request.ExpiresInHours);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO conversation_shares(id,tenant_id,conversation_id,created_by_user_id,token_sha256,token_last4,token_sealed,include_tools,expires_utc,view_count,active,created_utc,updated_utc) VALUES(@id,@tenant,@conversation,@user,@hash,@last4,@sealed,@tools,@expires,0,1,@now,@now)", token, P("id", id, "tenant", context.TenantId, "conversation", conversationId, "user", context.UserId, "hash", SecretTokens.Hash(secret), "last4", SecretTokens.Last4(secret), "sealed", _Database.Sealer?.Seal(secret) ?? "", "tools", request.IncludeToolCalls ? 1 : 0, "expires", SessionStore.Stamp(expires))).ConfigureAwait(false);
                await AuditLog.WriteAsync(connection, transaction, new AuditEvent { TenantId = context.TenantId, UserId = context.UserId, Operation = "ShareCreated", Outcome = "success", ResourceType = "conversation", ResourceId = conversationId, Reason = "Public link " + id + " until " + SessionStore.Stamp(expires), PrincipalType = context.PrincipalType, SessionId = context.SessionId, CredentialId = context.CredentialId, CorrelationId = context.CorrelationId, SourceIp = context.SourceIp, StatusCode = 201 }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            ConversationShare share = (await ListAsync(context, conversationId, token).ConfigureAwait(false)).First(item => item.Id == id);
            string path = "/share/" + secret;
            return new ConversationShareCreated { Share = share, Token = secret, Path = path, Url = baseUrl.TrimEnd('/') + path };
        }

        /// <inheritdoc />
        public async Task<ConversationShareCreated> RevealAsync(RequestContext context, string conversationId, string shareId, string baseUrl, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(baseUrl);
            ConversationShare share = (await ListAsync(context, conversationId, token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == shareId) ?? throw new DomainException(404, "share_not_found", "This link does not exist.");
            if (!share.Active) throw new DomainException(409, "share_inactive", "This link has expired or was revoked. Create a new one.");
            string stored;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
                stored = Convert.ToString(await ScalarAsync(connection, null, "SELECT token_sealed FROM conversation_shares WHERE tenant_id=@tenant AND id=@id", token, P("tenant", context.TenantId, "id", shareId)).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
            string? secret = stored.Length > 0 ? _Database.Sealer?.Unseal(stored) : null;
            if (secret == null) throw new DomainException(409, "not_revealable", "This link cannot be shown again. Create a new link and revoke this one.");
            await _Database.WriteAsync((connection, transaction) => AuditLog.WriteAsync(connection, transaction, new AuditEvent { TenantId = context.TenantId, UserId = context.UserId, Operation = "ShareRevealed", Outcome = "success", ResourceType = "conversation", ResourceId = conversationId, Reason = "Public link " + shareId, PrincipalType = context.PrincipalType, SessionId = context.SessionId, CredentialId = context.CredentialId, CorrelationId = context.CorrelationId, SourceIp = context.SourceIp, StatusCode = 200 }, token), token).ConfigureAwait(false);
            string path = "/share/" + secret;
            return new ConversationShareCreated { Share = share, Token = secret, Path = path, Url = baseUrl.TrimEnd('/') + path };
        }

        /// <inheritdoc />
        public async Task RevokeAsync(RequestContext context, string conversationId, string shareId, CancellationToken token = default)
        {
            RequireWrite(context);
            await _Database.ReadConversationAsync(context, conversationId, token).ConfigureAwait(false);
            await _Database.WriteAsync(async (connection, transaction) =>
            {
                if (await ExecuteAsync(connection, transaction, "UPDATE conversation_shares SET active=0,revoked_utc=COALESCE(revoked_utc,@now),updated_utc=@now WHERE tenant_id=@tenant AND conversation_id=@conversation AND id=@id", token, P("tenant", context.TenantId, "conversation", conversationId, "id", shareId)).ConfigureAwait(false) == 0) throw new DomainException(404, "share_not_found", "This link does not exist.");
                await AuditLog.WriteAsync(connection, transaction, new AuditEvent { TenantId = context.TenantId, UserId = context.UserId, Operation = "ShareRevoked", Outcome = "success", ResourceType = "conversation", ResourceId = conversationId, Reason = "Public link " + shareId, PrincipalType = context.PrincipalType, SessionId = context.SessionId, CredentialId = context.CredentialId, CorrelationId = context.CorrelationId, SourceIp = context.SourceIp, StatusCode = 200 }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<SharedTranscript> ReadAsync(string shareToken, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(shareToken);
            DomainException missing = new DomainException(404, "share_not_found", "This link is not valid. It may have expired or been revoked.");
            if (shareToken.Length < 20 || shareToken.Length > 128) throw missing;
            string hash = SecretTokens.Hash(shareToken);
            string id = "", tenantId = "", conversationId = "", userId = ""; bool tools = false; DateTime expires;
            using (DbConnection connection = await _Database.OpenAsync(token).ConfigureAwait(false))
            using (DbCommand command = Command(connection, null, "SELECT s.* FROM conversation_shares s JOIN tenants t ON t.id=s.tenant_id WHERE s.token_sha256=@hash AND s.active=1 AND s.revoked_utc IS NULL AND s.expires_utc>@now AND t.active=1", P("hash", hash)))
            using (DbDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw missing;
                id = S(reader, "id"); tenantId = S(reader, "tenant_id"); conversationId = S(reader, "conversation_id"); userId = S(reader, "created_by_user_id");
                tools = IdentityMethods.Flag(reader, "include_tools"); expires = IdentityMethods.Time(reader, "expires_utc");
            }
            RequestContext shareContext = new RequestContext { TenantId = tenantId, UserId = userId, IsAuthenticated = true, PrincipalType = "Share" };
            Conversation conversation;
            try { conversation = await _Database.ReadConversationAsync(shareContext, conversationId, token).ConfigureAwait(false); }
            catch (DomainException ex) when (ex.StatusCode == 404) { throw missing; }
            List<DebateRun> runs = await _Database.Runs.EnumerateAsync(shareContext, conversationId, token).ConfigureAwait(false);
            List<Message> messages = await _Database.ReadMessagesAsync(shareContext, conversationId, token).ConfigureAwait(false);
            SharedTranscript transcript = new SharedTranscript { Title = conversation.Title, OpeningQuestion = conversation.OpeningQuestion, ExpiresUtc = expires, IncludeToolCalls = tools };
            foreach (DebateRun run in runs.OrderBy(item => item.CreatedUtc))
            {
                SharedRun shared = new SharedRun { Id = run.Id, State = run.State, Outcome = run.Outcome, Round = run.Round, MaxRounds = run.MaxRounds, Question = run.Question, ProtocolVersion = run.ProtocolVersion, CreatedUtc = run.CreatedUtc, Orchestrator = run.Orchestrator == null ? null : new SharedSeat { Id = run.Orchestrator.Id, Name = run.Orchestrator.Name } };
                shared.Participants.AddRange(run.Participants.Select(seat => new SharedSeat { Id = seat.Id, Name = seat.Name }));
                transcript.Runs.Add(shared);
                if (run.ProtocolVersion != 2) continue;
                for (int page = 1; page <= 50; page++)
                {
                    List<DebateProposal> proposals = await _Database.Debates.ReadProposalsAsync(shareContext, conversationId, run.Id, token, page).ConfigureAwait(false);
                    transcript.Proposals.AddRange(proposals.Select(proposal => new SharedProposal { Id = proposal.Id, RunId = run.Id, Content = proposal.Content, Superseded = proposal.Superseded, Agreements = proposal.Agreements.Select(vote => new SharedVote { ParticipantId = vote.ParticipantId, MessageId = vote.MessageId, Position = vote.Position, Rationale = vote.Rationale, Superseded = vote.Superseded }).ToList() }));
                    if (proposals.Count < 100) break;
                }
            }
            // Private directives (addressed to one seat) are moderator instructions, not part of the public record.
            transcript.Messages.AddRange(messages.Where(message => message.RecipientId == null && (tools || message.Kind != "tool")).Select(message => new SharedMessage { Id = message.Id, RunId = message.RunId ?? "", Speaker = message.Speaker, Kind = message.Kind, State = message.State, Content = message.Content, CreatedUtc = message.CreatedUtc }));
            await _Database.WriteAsync((connection, transaction) => ExecuteAsync(connection, transaction, "UPDATE conversation_shares SET view_count=view_count+1,last_viewed_utc=@now WHERE id=@id", token, P("id", id)), token).ConfigureAwait(false);
            return transcript;
        }

        private static ConversationShare Read(DbDataReader reader)
        {
            DateTime expires = IdentityMethods.Time(reader, "expires_utc"); DateTime? revoked = IdentityMethods.OptionalTime(reader, "revoked_utc");
            return new ConversationShare { Id = S(reader, "id"), ConversationId = S(reader, "conversation_id"), CreatedByUserId = S(reader, "created_by_user_id"), TokenLast4 = S(reader, "token_last4"), IncludeToolCalls = IdentityMethods.Flag(reader, "include_tools"), ExpiresUtc = expires, RevokedUtc = revoked, LastViewedUtc = IdentityMethods.OptionalTime(reader, "last_viewed_utc"), ViewCount = Convert.ToInt64(reader["view_count"], CultureInfo.InvariantCulture), Active = IdentityMethods.Flag(reader, "active") && revoked == null && expires > DateTime.UtcNow, CreatedUtc = IdentityMethods.Time(reader, "created_utc") };
        }
    }
}
