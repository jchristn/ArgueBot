namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Immutable per-run budgets and orchestrator policy. Models cannot increase these limits.</summary>
    public sealed class DebatePolicy
    {
        /// <summary>Participant response and ratification invocation limit, including failed attempts.</summary>
        public int MaxParticipantTurns { get; set; } = 8192;
        /// <summary>Question and synthesis invocation limit, including failed attempts.</summary>
        public int MaxOrchestrationCalls { get; set; } = 256;
        /// <summary>Summary invocation limit, including failed attempts.</summary>
        public int MaxCompactionCalls { get; set; } = 256;
        /// <summary>Conversation and local tool call limit for this run.</summary>
        public int MaxToolCalls { get; set; } = 8192;
        /// <summary>Elapsed deadline from run admission, including queues, intervention windows and pauses.</summary>
        public int MaxWallClockSeconds { get; set; } = 86400;
        /// <summary>Per-invocation character ceiling, further restricted by service and model capacity.</summary>
        public int MaxContextCharacters { get; set; } = 262144;
        /// <summary>Optional cumulative estimated initial-input token ceiling; zero disables it. This is an estimate, not provider accounting.</summary>
        public int MaxEstimatedInputTokens { get; set; } = 0;
        private string _ResourceAccess = "read";
        /// <summary>What the debate's agents may do with the tenant's resources: off (no resource tools), read (the default: list, search and read) or read_write (also create, update, edit and append).</summary>
        public string ResourceAccess { get { return _ResourceAccess; } set { ArgumentNullException.ThrowIfNull(value); _ResourceAccess = value; } }
        private List<string> _ResourceNames = new List<string>();
        /// <summary>Optional names of the only resources this debate's agents can see, up to 200. With resourceTags, a resource is visible when it matches either list. Both empty means every resource.</summary>
        public List<string> ResourceNames { get { return _ResourceNames; } set { ArgumentNullException.ThrowIfNull(value); _ResourceNames = value; } }
        private List<string> _ResourceTags = new List<string>();
        /// <summary>Optional tags, up to 16; resources with any of them are visible. Resources the agents create are visible to them for the rest of the debate.</summary>
        public List<string> ResourceTags { get { return _ResourceTags; } set { ArgumentNullException.ThrowIfNull(value); _ResourceTags = value; } }
        /// <summary>Most resource writes (create, update, edit and append) in one agent turn, 0 through 100. Default 8.</summary>
        public int MaxResourceWritesPerTurn { get; set; } = 8;
        /// <summary>Most UTF-8 bytes of resource text written in one agent turn, 0 through 1048576. Default 65536.</summary>
        public int MaxResourceWriteBytesPerTurn { get; set; } = 65536;
        /// <summary>Most resources the debate's agents may create in total, 0 through 1000. Default 50.</summary>
        public int MaxResourcesCreated { get; set; } = 50;
        /// <summary>Automatic retries for one step before the debate pauses, 0 through 10. Default 3. Covers correction requests after an invalid question, proposal, vote or summary, and repeats after a transient failure (rate limit, provider outage, dropped connection, agent exit or turn timeout), with a growing wait between repeats. 0 pauses at the first failure. A repeated turn can repeat external work.</summary>
        public int MaxStepRetries { get; set; } = 3;
        private string _UserQuestions = "all";
        /// <summary>Which agents may ask the user questions with ask_user: off, orchestrator (the orchestrator and a follow-up answerer only) or all (every seat). Default all.</summary>
        public string UserQuestions { get { return _UserQuestions; } set { ArgumentNullException.ThrowIfNull(value); _UserQuestions = value; } }
        private string _Intake = "ask_if_needed";
        /// <summary>Whether the debate opens with an intake step in which the orchestrator may ask the user about the question before round 1: off or ask_if_needed. Default ask_if_needed for new debates; debates created before 2.3.0 have off.</summary>
        public string Intake { get { return _Intake; } set { ArgumentNullException.ThrowIfNull(value); _Intake = value; } }
        /// <summary>Most questions the intake request may carry, 1 through 4. Default 4.</summary>
        public int MaxIntakeQuestions { get; set; } = 4;
        /// <summary>Seconds a waiting ask_user call stays open for the answer before the turn is parked and resumed later, 0 through 1800. Default 300. 0 parks at once.</summary>
        public int QuestionHoldSeconds { get; set; } = 300;
        /// <summary>Seconds after which an unanswered question is treated as skipped, 0 through 2592000. Default 0, which never expires.</summary>
        public int QuestionExpirySeconds { get; set; }
        /// <summary>Whether the debate continues by itself once every waiting question is resolved. When false it pauses for Resume. Default true.</summary>
        public bool ResumeOnAnswer { get; set; } = true;
        /// <summary>Most ask_user requests in one agent turn, 0 through 4. Default 2.</summary>
        public int MaxQuestionRequestsPerTurn { get; set; } = 2;
        /// <summary>Most ask_user requests in the whole debate, 0 through 200. Default 20.</summary>
        public int MaxQuestionRequestsPerRun { get; set; } = 20;
        private string _QuestionInstructions = "";
        /// <summary>Additional neutral question-selection guidance, up to 4000 characters.</summary>
        public string QuestionInstructions { get { return _QuestionInstructions; } set { ArgumentNullException.ThrowIfNull(value); _QuestionInstructions = value; } }
        private string _SynthesisInstructions = "";
        /// <summary>Additional proposal presentation guidance, up to 4000 characters.</summary>
        public string SynthesisInstructions { get { return _SynthesisInstructions; } set { ArgumentNullException.ThrowIfNull(value); _SynthesisInstructions = value; } }
        private string _SummaryInstructions = "";
        /// <summary>Additional evidence-preservation guidance, up to 4000 characters.</summary>
        public string SummaryInstructions { get { return _SummaryInstructions; } set { ArgumentNullException.ThrowIfNull(value); _SummaryInstructions = value; } }
    }
}
