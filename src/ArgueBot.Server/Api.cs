namespace ArgueBot.Server
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using ArgueBot.Core.Models;
    using ArgueBot.Core.Services;
    using WatsonWebserver.Core;

    internal static class Api
    {
        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        internal static RequestContext Context(HttpContextBase http) { return http.Metadata as RequestContext ?? throw new DomainException(401, "unauthenticated", "Authentication required."); }
        internal static string Parameter(HttpContextBase http, string name) { return http.Request.Url.Parameters[name] ?? throw new DomainException(400, "missing_parameter", "Required route parameter is missing."); }
        internal static T Body<T>(HttpContextBase http)
        {
            if (http.Request.DataAsBytes.Length > 1048576) throw new DomainException(413, "body_too_large", "Request exceeds one MiB.");
            return JsonSerializer.Deserialize<T>(http.Request.DataAsString, Json) ?? throw new DomainException(400, "invalid_body", "A JSON object is required.");
        }
        internal static async Task SendAsync(HttpContextBase http, object value, int status = 200)
        {
            http.Response.StatusCode = status; http.Response.ContentType = "application/json";
            await http.Response.Send(JsonSerializer.Serialize(value, Json), http.Token).ConfigureAwait(false);
        }
        internal static async Task GuardAsync(HttpContextBase http, Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (DomainException ex) { await SendAsync(http, new ApiError { Code = ex.Code, Message = ex.Message, RequestId = (http.Metadata as RequestContext)?.CorrelationId ?? "" }, ex.StatusCode).ConfigureAwait(false); }
            catch (JsonException) { await SendAsync(http, new ApiError { Code = "invalid_json", Message = "Request JSON does not match the documented contract." }, 400).ConfigureAwait(false); }
            catch (ArgumentException) { await SendAsync(http, new ApiError { Code = "invalid_argument", Message = "A value is outside the allowed range or format." }, 400).ConfigureAwait(false); }
            catch (OperationCanceledException) when (http.Token.IsCancellationRequested) { }
        }
    }
}
