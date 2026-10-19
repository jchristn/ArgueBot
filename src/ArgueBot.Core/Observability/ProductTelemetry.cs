namespace ArgueBot.Core.Observability
{
    using System.Diagnostics.Metrics;
    using System.Diagnostics;

    /// <summary>Dependency-free application instrumentation. Only bounded operation/outcome dimensions belong on metrics.</summary>
    public static class ProductTelemetry
    {
        /// <summary>Application meter collected by the process composition root.</summary>
        public static readonly Meter Meter = new Meter("ArgueBot", "0.1.0");
        /// <summary>Application spans collected with Watson and PolyPrompt spans.</summary>
        public static readonly ActivitySource Activities = new ActivitySource("ArgueBot", "0.1.0");
        /// <summary>Completed database writes by provider and outcome.</summary>
        public static readonly Counter<long> StorageWrites = Meter.CreateCounter<long>("arguebot.storage.writes");
        /// <summary>Database write duration in seconds.</summary>
        public static readonly Histogram<double> StorageDuration = Meter.CreateHistogram<double>("arguebot.storage.write.duration", "s");
        /// <summary>Generation attempts by runner kind and outcome.</summary>
        public static readonly Counter<long> Generations = Meter.CreateCounter<long>("arguebot.runner.generations");
        /// <summary>Full generation duration in seconds.</summary>
        public static readonly Histogram<double> GenerationDuration = Meter.CreateHistogram<double>("arguebot.runner.duration", "s");
        /// <summary>Observed time to first answer text in seconds.</summary>
        public static readonly Histogram<double> FirstTextDuration = Meter.CreateHistogram<double>("arguebot.runner.first_text.duration", "s");
        /// <summary>Durably audited background execution denials with bounded reason-code labels.</summary>
        public static readonly Counter<long> ExecutionDenials = Meter.CreateCounter<long>("arguebot.execution.denials");
        /// <summary>Replay cursors requiring fresh snapshots, with bounded provider labels.</summary>
        public static readonly Counter<long> ReplayGaps = Meter.CreateCounter<long>("arguebot.replay.gaps");
        /// <summary>Committed replay events removed by retention. Domain transcript and audit data are unaffected.</summary>
        public static readonly Counter<long> EventsPruned = Meter.CreateCounter<long>("arguebot.replay.events_pruned");
        /// <summary>Accepted run starts.</summary>
        public static readonly Counter<long> Runs = Meter.CreateCounter<long>("arguebot.runs.started");
    }
}
