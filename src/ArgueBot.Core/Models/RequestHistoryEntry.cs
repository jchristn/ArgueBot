namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>RequestHistoryEntry operational contract.</summary>
    public sealed class RequestHistoryEntry
    {
        private string _Id = "";
        private string _PrincipalName = "anonymous";
        private string _Method = "";
        private string _Path = "";
        private string _RawRequestBody = "";
        private string _RawResponseBody = "";
        private string _CorrelationId = "";
        private string _TraceId = "";
        private List<CapturedHeader> _Headers = new List<CapturedHeader>();

        /// <summary>Stable capture identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        /// <summary>Resolved tenant, or null for anonymous system traffic.</summary>
        [JsonIgnore]
        public string? TenantId { get; set; }
        /// <summary>Resolved user if authenticated.</summary>
        [JsonIgnore]
        public string? UserId { get; set; }
        /// <summary>Safe principal display name.</summary>
        [JsonIgnore]
        public string PrincipalName { get { return _PrincipalName; } set { ArgumentNullException.ThrowIfNull(value); _PrincipalName = value; } }
        /// <summary>HTTP method.</summary>
        public string Method { get { return _Method; } set { ArgumentNullException.ThrowIfNull(value); _Method = value; } }
        /// <summary>Route path without query credentials.</summary>
        public string Path { get { return _Path; } set { ArgumentNullException.ThrowIfNull(value); _Path = value; } }
        /// <summary>HTTP response status.</summary>
        public int Status { get; set; }
        /// <summary>Elapsed milliseconds.</summary>
        public long DurationMs { get; set; }
        /// <summary>Original request size.</summary>
        public long RequestBytes { get; set; }
        /// <summary>Original response size where known; minus one means streamed or unknown.</summary>
        public long ResponseBytes { get; set; }
        /// <summary>Redacted diagnostic request prefix; explicitly unmanaged content.</summary>
        public string RawRequestBody { get { return _RawRequestBody; } set { ArgumentNullException.ThrowIfNull(value); _RawRequestBody = value; } }
        /// <summary>Redacted diagnostic response prefix; explicitly unmanaged content.</summary>
        public string RawResponseBody { get { return _RawResponseBody; } set { ArgumentNullException.ThrowIfNull(value); _RawResponseBody = value; } }
        /// <summary>Whether request capture is a prefix.</summary>
        public bool RequestTruncated { get; set; }
        /// <summary>Whether response capture is a prefix.</summary>
        public bool ResponseTruncated { get; set; }
        /// <summary>Request correlation ID.</summary>
        public string CorrelationId { get { return _CorrelationId; } set { ArgumentNullException.ThrowIfNull(value); _CorrelationId = value; } }
        /// <summary>Trace correlation ID.</summary>
        public string TraceId { get { return _TraceId; } set { ArgumentNullException.ThrowIfNull(value); _TraceId = value; } }
        /// <summary>Request receipt timestamp.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>Redacted structured headers.</summary>
        public List<CapturedHeader> Headers { get { return _Headers; } set { ArgumentNullException.ThrowIfNull(value); _Headers = value; } }
    }
}
