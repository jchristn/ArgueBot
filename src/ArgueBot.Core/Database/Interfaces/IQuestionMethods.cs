namespace ArgueBot.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    /// <summary>Typed database boundary for questions agents ask the user. Every read and write is constrained to the caller's tenant.</summary>
    public interface IQuestionMethods
    {
        /// <summary>Store a validated ask_user call, or return the existing request when a harness repeats the same call in one attempt.</summary>
        /// <exception cref="ArgueBot.Core.Services.DomainException">409 question_limit when the turn or debate limit is reached.</exception>
        Task<QuestionAskResult> CreateAsync(RequestContext context, QuestionAsk ask, CancellationToken token = default);

        /// <summary>Read one request with its questions, options and answers.</summary>
        /// <exception cref="ArgueBot.Core.Services.DomainException">404 not_found, including requests in another tenant.</exception>
        Task<QuestionRequest> ReadAsync(RequestContext context, string id, CancellationToken token = default);

        /// <summary>Read a page of requests, open requests first and then newest first.</summary>
        Task<QuestionPage> EnumerateAsync(RequestContext context, QuestionFilter filter, CancellationToken token = default);

        /// <summary>Every request of one run, oldest first.</summary>
        Task<List<QuestionRequest>> RunRequestsAsync(RequestContext context, string conversationId, string runId, CancellationToken token = default);

        /// <summary>Open question counts for the caller and the tenant.</summary>
        Task<QuestionSummary> SummaryAsync(RequestContext context, CancellationToken token = default);

        /// <summary>Whether the run has an open request whose turn waits for it.</summary>
        Task<bool> HasOpenWaitingAsync(RequestContext context, string runId, CancellationToken token = default);

        /// <summary>Answer every question of an open request. The first resolution wins; a repeated submit with the same idempotency key returns the stored result.</summary>
        /// <exception cref="ArgueBot.Core.Services.DomainException">400 invalid_answer; 404 not_found; 409 question_already_resolved.</exception>
        Task<QuestionRequest> AnswerAsync(RequestContext context, string id, QuestionAnswerRequest request, CancellationToken token = default);

        /// <summary>Skip a whole open request so the agent continues on stated assumptions.</summary>
        /// <exception cref="ArgueBot.Core.Services.DomainException">404 not_found; 409 question_already_resolved.</exception>
        Task<QuestionRequest> SkipAsync(RequestContext context, string id, QuestionSkipRequest request, CancellationToken token = default);

        /// <summary>Record whether a live tool call is waiting (held), the turn has ended (parked) or neither (none).</summary>
        Task SetHoldStateAsync(RequestContext context, string id, string holdState, CancellationToken token = default);

        /// <summary>Record the attempt that resumed a parked turn with the answer.</summary>
        Task LinkResumeAsync(RequestContext context, string id, string attemptId, CancellationToken token = default);

        /// <summary>Resolve open requests whose expiry has passed. Returns the requests that expired, across tenants, for the service to wake or resume their runs.</summary>
        Task<List<QuestionRequest>> ExpireDueAsync(CancellationToken token = default);

        /// <summary>Cancel every open request of a stopped run.</summary>
        Task<int> CancelOpenAsync(RequestContext context, string runId, CancellationToken token = default);

        /// <summary>Question changes in the caller's tenant after a cursor, oldest first. An empty cursor starts at the newest change, so a new stream reports only later changes.</summary>
        /// <exception cref="ArgueBot.Core.Services.DomainException">400 invalid_cursor.</exception>
        Task<QuestionEventPage> ReadEventsAsync(RequestContext context, string after, int limit, CancellationToken token = default);

        /// <summary>Park every held request, for example at startup, when no tool call can still be waiting.</summary>
        Task<int> ParkHeldAsync(CancellationToken token = default);
    }
}
