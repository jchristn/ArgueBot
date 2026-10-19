namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>DebateRun transport and domain data.</summary>
    public sealed class DebateRun
    {
        /// <summary>Stable execution identifier.</summary>
        public string Id { get; set; } = "";

        /// <summary>Containing conversation.</summary>
        public string ConversationId { get; set; } = "";

        /// <summary>Execution state.</summary>
        public string State { get; set; } = "paused";

        /// <summary>Completion reason.</summary>
        public string Outcome { get; set; } = "";

        /// <summary>Current round.</summary>
        public int Round { get; set; } = 1;

        /// <summary>Next participant index.</summary>
        public int SpeakerIndex { get; set; }

        /// <summary>Round limit.</summary>
        public int MaxRounds { get; set; } = 5;

        /// <summary>Pause between turns.</summary>
        public int InterventionSeconds { get; set; } = 10;

        /// <summary>Optimistic run revision.</summary>
        public long Revision { get; set; }

        /// <summary>Immutable runner snapshots.</summary>
        public List<RunnerProfile> Participants { get; set; } = new List<RunnerProfile>();

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

    }
}
