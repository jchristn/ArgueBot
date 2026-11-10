namespace ArgueBot.Server.Services
{
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver.Core;

    internal sealed class RequestCapture
    {
        private readonly DatabaseDriverBase _Database;
        private readonly ServiceSettings _Settings;
        private readonly string _LogFile;
        private DateTime _LastPrune = DateTime.MinValue;
        internal RequestCapture(DatabaseDriverBase database, ServiceSettings settings, ArgueBotPaths paths) { _Database = database; _Settings = settings; _LogFile = Path.Combine(paths.Home, "logs", "capture-errors.log"); }
        internal async Task CaptureAsync(HttpContextBase http)
        {
            try
            {
                RequestContext context = http.Metadata as RequestContext ?? new RequestContext();
                if (!_Settings.RequestHistoryEnabled) return;
                int limit = Math.Clamp(_Settings.RequestHistoryBodyBytes, 0, 1048576);
                string capturedPath = http.Request.Url.RawWithoutQuery;
                // Interfaces poll the question summary and change feed every few seconds; recording each read would bury real requests.
                if (http.Request.Method == HttpMethod.GET && (capturedPath == "/v2.0/api/questions/events" || capturedPath == "/v2.0/api/questions/summary")) return;
                // Secrets, sign-in, session tokens, bearer-token credentials and share links never reach request history, even redacted.
                bool credentialBody = capturedPath.StartsWith("/v2.0/api/configuration/credentials", StringComparison.Ordinal) || capturedPath.StartsWith("/v2.0/api/configuration/secrets", StringComparison.Ordinal) || capturedPath == "/v2.0/api/token" || capturedPath.StartsWith("/v2.0/api/token/", StringComparison.Ordinal) || capturedPath.Contains("/credentials", StringComparison.Ordinal) || capturedPath.Contains("/shares", StringComparison.Ordinal) || capturedPath.StartsWith("/v2.0/api/users", StringComparison.Ordinal) || capturedPath.StartsWith("/v2.0/api/me/password", StringComparison.Ordinal);
                string request = credentialBody ? "[credential body excluded]" : CaptureRedactor.Body(http.Request.DataAsString ?? "", http.Request.ContentType ?? "");
                string response = ""; long responseBytes = http.Response.ServerSentEvents ? -1 : http.Response.ContentLength;
                if (!http.Response.ServerSentEvents && (http.Response.ContentType ?? "").Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    try { string raw = http.Response.DataAsString ?? ""; responseBytes = Encoding.UTF8.GetByteCount(raw); response = credentialBody ? "[credential body excluded]" : CaptureRedactor.Body(raw, http.Response.ContentType ?? ""); }
                    catch (ObjectDisposedException) { response = "[streamed response omitted]"; }
                }
                // Resource bodies can be large; above the history limit keep a note instead of a partial document.
                bool resourcePath = capturedPath == "/v2.0/api/resources" || capturedPath.StartsWith("/v2.0/api/resources/", StringComparison.Ordinal);
                bool requestOmitted = resourcePath && Encoding.UTF8.GetByteCount(request) > limit;
                bool responseOmitted = resourcePath && Encoding.UTF8.GetByteCount(response) > limit;
                if (requestOmitted) request = "[resource body over the request history limit omitted]";
                if (responseOmitted) response = "[resource body over the request history limit omitted]";
                RequestHistoryEntry entry = new RequestHistoryEntry
                {
                    TenantId = context.IsAuthenticated ? context.TenantId : null,
                    UserId = context.IsAuthenticated ? context.UserId : null,
                    PrincipalName = context.IsAuthenticated ? context.UserId : "anonymous",
                    Method = http.Request.Method.ToString(),
                    // A public share token is in the path; keep only a placeholder so request history cannot reopen the link.
                    Path = System.Text.RegularExpressions.Regex.Replace(capturedPath, @"^(/v2\.0/api/shares/|/share/)[^/]+", "$1[token]"),
                    Status = http.Response.StatusCode,
                    DurationMs = context.StartedTimestamp > 0 ? (long)Stopwatch.GetElapsedTime(context.StartedTimestamp).TotalMilliseconds : 0,
                    RequestBytes = http.Request.ContentLength,
                    ResponseBytes = responseBytes,
                    RawRequestBody = Prefix(request, limit),
                    RawResponseBody = Prefix(response, limit),
                    RequestTruncated = requestOmitted || Encoding.UTF8.GetByteCount(request) > limit,
                    ResponseTruncated = responseOmitted || http.Response.ServerSentEvents || Encoding.UTF8.GetByteCount(response) > limit,
                    CorrelationId = context.CorrelationId,
                    TraceId = Activity.Current?.TraceId.ToString() ?? "",
                    CreatedUtc = context.ReceivedUtc
                };
                Headers(entry, http.Request.Headers, false); Headers(entry, http.Response.Headers, true);
                await _Database.History.InsertAsync(entry, CancellationToken.None).ConfigureAwait(false);
                if (DateTime.UtcNow - _LastPrune > TimeSpan.FromHours(1))
                {
                    _LastPrune = DateTime.UtcNow;
                    await _Database.History.PruneAsync(DateTime.UtcNow.AddDays(-Math.Clamp(_Settings.RequestHistoryRetentionDays, 1, 3650)), CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Capture failures are best-effort and must not change a successful user response.
                try { await File.AppendAllTextAsync(_LogFile, DateTime.UtcNow.ToString("O") + " " + ex.GetType().Name + "\n").ConfigureAwait(false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        private static string Prefix(string value, int maximum)
        {
            if (Encoding.UTF8.GetByteCount(value) <= maximum) return value;
            int count = Math.Min(value.Length, maximum);
            while (count > 0 && Encoding.UTF8.GetByteCount(value.AsSpan(0, count)) > maximum) count--;
            if (count > 0 && char.IsHighSurrogate(value[count - 1])) count--;
            return value.Substring(0, count);
        }
        private static void Headers(RequestHistoryEntry entry, NameValueCollection headers, bool response)
        {
            foreach (string? key in headers.AllKeys) if (key != null) entry.Headers.Add(new CapturedHeader { Name = key, Value = CaptureRedactor.Sensitive(key) ? "[redacted]" : (CaptureRedactor.Text(headers[key]) ?? ""), Response = response });
        }
    }
}
