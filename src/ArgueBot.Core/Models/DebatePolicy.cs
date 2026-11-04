namespace ArgueBot.Core.Models
{
    using System;

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
