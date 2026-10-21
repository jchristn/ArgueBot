#if NET10_0_OR_GREATER
namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;

    internal sealed class TerminalNavigationHandler : HttpMessageHandler
    {
        internal string? DeletedId { get; private set; }
        internal bool DelayNextRead { get; set; }
        internal TaskCompletionSource ReadStarted { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseRead { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            object response;
            if (path == "/v1.0/api/conversations") response = new Page { Items = new List<Conversation> { new Conversation { Id = "alpha", Title = "Alpha" }, new Conversation { Id = "beta", Title = "Beta" } }, TotalCount = 2, PageNumber = 1, PageSize = 25 };
            else if (path.EndsWith("/messages", StringComparison.Ordinal))
            {
                if (DelayNextRead) { DelayNextRead = false; ReadStarted.TrySetResult(); await ReleaseRead.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
                response = new MessagePage { Items = new List<Message> { new Message { Id = "fixture-message", Content = path.Contains("/alpha/", StringComparison.Ordinal) ? "History for Alpha" : "History for Beta" } }, TotalCount = 1, PageNumber = 1, PageSize = 25 };
            }
            else if (path == "/v1.0/api/conversations/alpha" || path == "/v1.0/api/conversations/beta")
            {
                string id = path.EndsWith("/alpha", StringComparison.Ordinal) ? "alpha" : "beta";
                if (request.Method == HttpMethod.Delete) { DeletedId = id; response = new DeletionResponse(); }
                else response = new Conversation { Id = id, Title = id == "alpha" ? "Alpha" : "Beta" };
            }
            else throw new InvalidOperationException("Unexpected terminal navigation request: " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
        }
    }
}
#endif
