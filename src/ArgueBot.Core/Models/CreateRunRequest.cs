namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>CreateRunRequest transport and domain data.</summary>
    public sealed class CreateRunRequest
    {
        private DebatePolicy _Policy = new DebatePolicy();
        /// <summary>Frozen execution limits and orchestration instructions for protocol 2.</summary>
        public DebatePolicy Policy { get { return _Policy; } set { ArgumentNullException.ThrowIfNull(value); _Policy = value; } }

        private string _Mode = "debate";
        private string _Question = "";
        private List<string> _ParticipantIds = new List<string>();
        private string _IdempotencyKey = "";

        /// <summary>Run purpose: debate or followup. Follow-up uses exactly one participant.</summary>
        public string Mode { get { return _Mode; } set { ArgumentNullException.ThrowIfNull(value); _Mode = value; } }
        /// <summary>Debate question frozen for this run; blank uses the conversation title. Follow-up questions are persisted as messages.</summary>
        public string Question { get { return _Question; } set { ArgumentNullException.ThrowIfNull(value); _Question = value; } }

        /// <summary>Ordered profiles.</summary>
        public List<string> ParticipantIds { get { return _ParticipantIds; } set { ArgumentNullException.ThrowIfNull(value); _ParticipantIds = value; } }

        /// <summary>Round limit.</summary>
        public int MaxRounds { get; set; } = 5;

        /// <summary>Intervention countdown.</summary>
        public int InterventionSeconds { get; set; } = 10;

        /// <summary>Unique command key.</summary>
        public string IdempotencyKey { get { return _IdempotencyKey; } set { ArgumentNullException.ThrowIfNull(value); _IdempotencyKey = value; } }

        /// <summary>Debate protocol: 1 for historical compatibility, 2 for model-guided unanimous debates.</summary>
        public int ProtocolVersion { get; set; } = 1;
        private string _OrchestratorId = "";
        /// <summary>Reusable orchestrator profile, required for protocol 2 debates.</summary>
        public string OrchestratorId { get { return _OrchestratorId; } set { System.ArgumentNullException.ThrowIfNull(value); _OrchestratorId = value; } }

    }
}
