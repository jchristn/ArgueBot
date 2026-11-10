namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>DebateRun transport and domain data.</summary>
    public sealed class DebateRun
    {
        private DebatePolicy _Policy = new DebatePolicy();
        /// <summary>Frozen execution limits and orchestration instructions for protocol 2.</summary>
        public DebatePolicy Policy { get { return _Policy; } set { ArgumentNullException.ThrowIfNull(value); _Policy = value; } }
        /// <summary>Durable per-purpose invocation and tool reservations.</summary>
        public DebateUsage Usage { get; set; } = new DebateUsage();

        private string _Id = "";
        private string _ConversationId = "";
        private string _State = "paused";
        private string _Outcome = "";
        private List<RunnerProfile> _Participants = new List<RunnerProfile>();

        private string _Question = "";
        /// <summary>Debate question frozen when this run was created; follow-up runs retain the original conversation title. Renaming the conversation never changes historical prompts. Pre-schema-9 runs use the title available at migration.</summary>
        public string Question { get { return _Question; } set { ArgumentNullException.ThrowIfNull(value); _Question = value; } }

        /// <summary>Run purpose derived from its immutable participant count.</summary>
        public string Mode { get { return ExplicitMode.Length > 0 ? ExplicitMode : Participants.Count == 1 ? "followup" : "debate"; } }

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

        /// <summary>Persisted debate protocol, independent of API and product versions.</summary>
        public int ProtocolVersion { get; set; } = 1;
        /// <summary>Current question, response, synthesis, ratification or compaction phase.</summary>
        public string Phase { get; set; } = "response";
        /// <summary>Current moderator-context revision.</summary>
        public long ContextEpoch { get; set; }
        /// <summary>Current active roster revision.</summary>
        public long RosterRevision { get; set; } = 1;
        /// <summary>Immutable orchestrator configuration snapshot.</summary>
        public RunnerProfile? Orchestrator { get; set; }
        /// <summary>Current candidate proposal identifier.</summary>
        public string ProposalId { get; set; } = "";
        /// <summary>Current pointed question from the orchestrator.</summary>
        public string CurrentQuestion { get; set; } = "";

        private List<RunPrompt> _Prompts = new List<RunPrompt>();
        /// <summary>Prompt text this debate froze for each step at start. Empty for runs created before prompts were stored; those use the factory text.</summary>
        public List<RunPrompt> Prompts { get { return _Prompts; } set { ArgumentNullException.ThrowIfNull(value); _Prompts = value; } }
        /// <summary>Current ratification candidate text.</summary>
        public string ProposalText { get; set; } = "";
        /// <summary>Original run purpose, retained independently of roster changes.</summary>
        public string ExplicitMode { get; set; } = "";
        /// <summary>Phase resumed after successful context compaction.</summary>
        public string ResumePhase { get; set; } = "";
        internal RunnerProfile? ExecutionProfile { get; set; }
        internal string ResourceCatalog { get; set; } = "";
        /// <summary>Answered questions rendered for the opt-in {answered_questions} placeholder. Derived per invocation.</summary>
        internal string AnsweredQuestions { get; set; } = "";

        private string _CompactionRecipientId = "";
        /// <summary>Private recipient partition for compaction; empty means shared evidence.</summary>
        public string CompactionRecipientId { get { return _CompactionRecipientId; } set { System.ArgumentNullException.ThrowIfNull(value); _CompactionRecipientId = value; } }
        /// <summary>True while a moderator-requested rebuild compacts original evidence; the prior shared summary stays active until the replacement validates.</summary>
        public bool RebuildFromOriginals { get; set; }
        /// <summary>Seconds the run has spent waiting for the user's answers. Excluded from the wall-clock budget.</summary>
        public long WaitingSeconds { get; set; }
        /// <summary>When the run started waiting for an answer; null while it is not waiting.</summary>
        public DateTime? AwaitingSinceUtc { get; set; }
        private string _ResumeQuestionId = "";
        /// <summary>Question whose answer the next attempt resumes from, when the previous attempt of this step ended waiting for the user. Derived, not stored.</summary>
        public string ResumeQuestionId { get { return _ResumeQuestionId; } set { System.ArgumentNullException.ThrowIfNull(value); _ResumeQuestionId = value; } }
        private string _ResumeAttemptId = "";
        /// <summary>The attempt that ended waiting for the user and that the next attempt resumes. Derived, not stored.</summary>
        public string ResumeAttemptId { get { return _ResumeAttemptId; } set { System.ArgumentNullException.ThrowIfNull(value); _ResumeAttemptId = value; } }

        /// <summary>True when the previous step returned invalid structured output and the next attempt is a correction request within the step retry limit.</summary>
        public bool RepairPending { get; set; }

        /// <summary>Number of the pending correction request, starting at 1.</summary>
        internal int RepairNumber { get; set; }
    }
}
