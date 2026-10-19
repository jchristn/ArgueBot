namespace ArgueBot.Server.Services
{
    using System.Collections.Generic;
    using ArgueBot.Core.Configuration;
    using Radiant;

    internal static class TelemetryHost
    {
        internal static RadiantHost Start(TelemetrySettings settings)
        {
            RadiantSettings pipeline = new RadiantSettings { ServiceName = "arguebot-server", Enable = settings.Enabled };
            pipeline.Sources.MeterNames = new List<string> { "Watson", "ArgueBot", "PolyPrompt" };
            pipeline.Sources.ActivitySourceNames = new List<string> { "Watson", "ArgueBot", "PolyPrompt" };
            pipeline.Otlp.Enable = settings.ExportEnabled; pipeline.Otlp.Endpoint = settings.Endpoint;
            pipeline.Prometheus.Enable = false;
            foreach (string name in new string[] { "http.server.request.duration", "arguebot.storage.write.duration", "arguebot.runner.duration", "arguebot.runner.first_text.duration" })
            {
                pipeline.Metrics.Definitions.Add(new MetricDefinition { Key = name, Kind = MetricKindEnum.Histogram, Unit = "s", Buckets = new double[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300 } });
            }
            return RadiantHost.Start(pipeline);
        }
    }
}
