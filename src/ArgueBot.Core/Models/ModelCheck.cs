namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Observed model-check result tied to exact nonsecret configuration revisions.</summary>
    public sealed class ModelCheck
    {
        /// <summary>Stable check identity.</summary>
        public string Id { get; set; } = "";
        /// <summary>Reusable model identity.</summary>
        public string ModelId { get; set; } = "";
        /// <summary>Frozen model revision.</summary>
        public long ModelRevision { get; set; }
        /// <summary>Frozen endpoint or harness identity.</summary>
        public string TransportId { get; set; } = "";
        /// <summary>Frozen endpoint or harness revision.</summary>
        public long TransportRevision { get; set; }
        /// <summary>queued, running, completed, failed, cancelled or uncertain.</summary>
        public string State { get; set; } = "queued";
        /// <summary>Whether the explicit request included a tool probe.</summary>
        public bool CheckTools { get; set; }
        /// <summary>A complete nonempty response was observed. This does not certify debate quality.</summary>
        public bool CompletionVerified { get; set; }
        /// <summary>The exact harmless tool probe was received and answered.</summary>
        public bool ToolsVerified { get; set; }
        /// <summary>Safe failure classification; provider bodies and diagnostics are excluded.</summary>
        public string ErrorCode { get; set; } = "";
        /// <summary>Numeric version reported by a successful known CLI executable check; empty means unverified.</summary>
        public string ExecutableVersion { get; set; } = "";
        /// <summary>Monotonic elapsed check duration, including endpoint queueing.</summary>
        public long DurationMilliseconds { get; set; }
        /// <summary>Acceptance time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>Last recorded state transition in UTC.</summary>
        public DateTime UpdatedUtc { get; set; }
        /// <summary>Service-only immutable execution snapshot.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public RunnerProfile Snapshot { get; set; } = new RunnerProfile();
        /// <summary>Service-only admission marker; never serialized.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool NewlyAccepted { get; set; }
    }
}
