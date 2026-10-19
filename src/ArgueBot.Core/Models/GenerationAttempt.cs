namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Durable external generation provenance. Concurrent calls require external synchronization.</summary>
    public sealed class GenerationAttempt
    {
        private string _Id = "";
        /// <summary>Attempt identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        private string _ConversationId = "";
        /// <summary>Containing conversation.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }
        private string _RunId = "";
        /// <summary>Containing run.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }
        private string _MessageId = "";
        /// <summary>Answer message, preserved even if generation is interrupted.</summary>
        public string MessageId { get { return _MessageId; } set { ArgumentNullException.ThrowIfNull(value); _MessageId = value; } }
        private string _ParticipantId = "";
        /// <summary>Immutable participant snapshot identifier.</summary>
        public string ParticipantId { get { return _ParticipantId; } set { ArgumentNullException.ThrowIfNull(value); _ParticipantId = value; } }
        /// <summary>Monotonic attempt number within this run.</summary>
        public int Number { get; set; } = 0;
        /// <summary>Debate round at attempt acceptance.</summary>
        public int Round { get; set; } = 1;
        /// <summary>Ordered participant position at acceptance.</summary>
        public int SpeakerIndex { get; set; } = 0;
        private string _State = "pending";
        /// <summary>Pending, streaming, completed, interrupted or failed. Acceptance precedes external invocation; interruption never proves the external operation did not run.</summary>
        public string State { get { return _State; } set { ArgumentNullException.ThrowIfNull(value); _State = value; } }
        private string _ErrorCode = "";
        /// <summary>Safe failure code without upstream response content.</summary>
        public string ErrorCode { get { return _ErrorCode; } set { ArgumentNullException.ThrowIfNull(value); _ErrorCode = value; } }
        private string _PromptVersion = "";
        /// <summary>Version of the deterministic system prompt template.</summary>
        public string PromptVersion { get { return _PromptVersion; } set { ArgumentNullException.ThrowIfNull(value); _PromptVersion = value; } }
        private string _PromptDigest = "";
        /// <summary>SHA-256 of the exact UTF-8 system prompt.</summary>
        public string PromptDigest { get { return _PromptDigest; } set { ArgumentNullException.ThrowIfNull(value); _PromptDigest = value; } }
        /// <summary>Exact UTF-16 character count of system prompt plus selected message content; this is not a token estimate.</summary>
        public long ContextCharacters { get; set; } = 0;
        /// <summary>Monotonic elapsed generation and durable stream flush time, zero if unavailable after a crash.</summary>
        public long DurationMilliseconds { get; set; } = 0;
        /// <summary>Acceptance time in UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Last lifecycle change in UTC.</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        private List<string> _ContextMessageIds = new List<string>();
        /// <summary>Exact ordered source messages selected for this attempt, including any one-recipient directives.</summary>
        public List<string> ContextMessageIds { get { return _ContextMessageIds; } set { ArgumentNullException.ThrowIfNull(value); _ContextMessageIds = value; } }
    }
}
