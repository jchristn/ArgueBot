namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>DebateRun transport and domain data.</summary>
    public sealed class DebateRun
    {
        private string _Id = "";
        private string _ConversationId = "";
        private string _State = "paused";
        private string _Outcome = "";
        private List<RunnerProfile> _Participants = new List<RunnerProfile>();

        private string _Question = "";
        /// <summary>Debate question frozen when this run was created; follow-up runs retain the original conversation title. Renaming the conversation never changes historical prompts. Pre-schema-9 runs use the title available at migration.</summary>
        public string Question { get { return _Question; } set { ArgumentNullException.ThrowIfNull(value); _Question = value; } }

        /// <summary>Run purpose derived from its immutable participant count.</summary>
        public string Mode { get { return Participants.Count == 1 ? "followup" : "debate"; } }

        /// <summary>Stable execution identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Containing conversation.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        /// <summary>Execution state.</summary>
        public string State { get { return _State; } set { ArgumentNullException.ThrowIfNull(value); _State = value; } }

        /// <summary>Completion reason.</summary>
        public string Outcome { get { return _Outcome; } set { ArgumentNullException.ThrowIfNull(value); _Outcome = value; } }

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
        public List<RunnerProfile> Participants { get { return _Participants; } set { ArgumentNullException.ThrowIfNull(value); _Participants = value; } }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

    }
}
