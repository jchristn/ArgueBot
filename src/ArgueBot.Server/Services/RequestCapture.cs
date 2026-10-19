namespace ArgueBot.Server.Services
{
    using System;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ArgueBot.Core.Configuration;
    using ArgueBot.Core.Database;
    using ArgueBot.Core.Models;
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
            if (!_Settings.RequestHistoryEnabled) return;
            try
            {
                RequestContext context = http.Metadata as RequestContext ?? new RequestContext();
                int limit = Math.Clamp(_Settings.RequestHistoryBodyBytes, 0, 1048576);
                string request = CaptureRedactor.Body(http.Request.DataAsString ?? "", http.Request.ContentType ?? "");
                string response = ""; long responseBytes = http.Response.ServerSentEvents ? -1 : http.Response.ContentLength;
                if (!http.Response.ServerSentEvents && (http.Response.ContentType ?? "").Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    try { string raw = http.Response.DataAsString ?? ""; responseBytes = Encoding.UTF8.GetByteCount(raw); response = CaptureRedactor.Body(raw, http.Response.ContentType ?? ""); }
                    catch (ObjectDisposedException) { response = "[streamed response omitted]"; }
                }
                RequestHistoryEntry entry = new RequestHistoryEntry
                {
                    TenantId = context.IsAuthenticated ? context.TenantId : null,
                    UserId = context.IsAuthenticated ? context.UserId : null,
                    PrincipalName = context.IsAuthenticated ? context.UserId : "anonymous",
                    Method = http.Request.Method.ToString(),
                    Path = http.Request.Url.RawWithoutQuery,
                    Status = http.Response.StatusCode,
                    DurationMs = context.StartedTimestamp > 0 ? (long)Stopwatch.GetElapsedTime(context.StartedTimestamp).TotalMilliseconds : 0,
                    RequestBytes = http.Request.ContentLength,
                    ResponseBytes = responseBytes,
                    RawRequestBody = Prefix(request, limit),
                    RawResponseBody = Prefix(response, limit),
                    RequestTruncated = Encoding.UTF8.GetByteCount(request) > limit,
                    ResponseTruncated = http.Response.ServerSentEvents || Encoding.UTF8.GetByteCount(response) > limit,
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
            foreach (string? key in headers.AllKeys) if (key != null) entry.Headers.Add(new CapturedHeader { Name = key, Value = CaptureRedactor.Sensitive(key) ? "[redacted]" : (headers[key] ?? ""), Response = response });
        }
    }
}
