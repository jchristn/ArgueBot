namespace ArgueBot.Core.Configuration
{
    using System;
    /// <summary>Process telemetry exports to a separately configured collector; no public scrape listener is opened by the library.</summary>
    public sealed class TelemetrySettings
    {
        private string _Endpoint = "http://127.0.0.1:4327";

        /// <summary>Enable the collector subscription, default true.</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Enable outbound OTLP to the local observability collector, default false outside Compose.</summary>
        public bool ExportEnabled { get; set; }
        /// <summary>OTLP gRPC endpoint, default 127.0.0.1:4327 to avoid the application port.</summary>
        public string Endpoint { get { return _Endpoint; } set { ArgumentNullException.ThrowIfNull(value); _Endpoint = value; } }
    }
}
