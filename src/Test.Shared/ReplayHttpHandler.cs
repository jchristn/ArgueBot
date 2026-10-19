namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http.Headers;
    using System.Net.Http;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using System.Threading;

    internal sealed class ReplayHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _Bytes;
        private readonly int _Chunk;
        internal string Cursor { get; private set; } = "";
        internal ReplayHttpHandler(string frames, int chunk = 4096) : this(Encoding.UTF8.GetBytes(frames), chunk) { }
        internal ReplayHttpHandler(byte[] bytes, int chunk = 4096) { _Bytes = bytes; _Chunk = chunk; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Headers.TryGetValues("Last-Event-ID", out IEnumerable<string>? values)) Cursor = values.Single();
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ChunkedReadStream(_Bytes, _Chunk)) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }
}
