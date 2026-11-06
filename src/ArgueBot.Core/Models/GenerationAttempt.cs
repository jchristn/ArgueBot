namespace ArgueBot.Core.Models
{
    using ArgueBot.Core.Services;
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
        /// <summary>Safe failure code without upstream response content. Per-turn deadlines use generation_timeout. Explicit interrupt, stop and orderly service shutdown use generation_interrupted, generation_stopped and service_shutdown; older interrupted attempts may have no code.</summary>
        public string ErrorCode { get { return _ErrorCode; } set { ArgumentNullException.ThrowIfNull(value); _ErrorCode = value; } }
        private string? _ErrorMessage;
        /// <summary>Safe cause and recovery guidance, without raw upstream payloads; historical diagnostics may refine the explanation.</summary>
        public string ErrorMessage { get { return _ErrorMessage ?? GenerationFailure.Explain(ErrorCode); } set { ArgumentNullException.ThrowIfNull(value); _ErrorMessage = value; } }
        private string _PromptVersion = "";
        /// <summary>Version of the deterministic system prompt template.</summary>
        public string PromptVersion { get { return _PromptVersion; } set { ArgumentNullException.ThrowIfNull(value); _PromptVersion = value; } }
        private string _PromptDigest = "";
        /// <summary>SHA-256 of the exact UTF-8 system prompt.</summary>
        public string PromptDigest { get { return _PromptDigest; } set { ArgumentNullException.ThrowIfNull(value); _PromptDigest = value; } }
        /// <summary>Exact UTF-16 character count of system prompt plus selected message content; this is not a token estimate.</summary>
        public long ContextCharacters { get; set; } = 0;
        /// <summary>Conservative initial-input token estimate from UTF-8 bytes plus message framing, not billed usage.</summary>
        public long EstimatedInputTokens { get; set; }
        /// <summary>Monotonic elapsed generation and durable stream flush time, zero if unavailable after a crash.</summary>
        public long DurationMilliseconds { get; set; } = 0;
        /// <summary>Acceptance time in UTC.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Last lifecycle change in UTC.</summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        private List<string> _ContextMessageIds = new List<string>();
        /// <summary>Exact ordered source messages selected for this attempt, including any one-recipient directives.</summary>
        public List<string> ContextMessageIds { get { return _ContextMessageIds; } set { ArgumentNullException.ThrowIfNull(value); _ContextMessageIds = value; } }
        /// <summary>response, question, synthesis, ratification or compaction.</summary>
        public string Purpose { get; set; } = "response";

        /// <summary>True when this attempt repeated a question, proposal, vote or summary after invalid structured output. The debate's step retry limit bounds the repairs.</summary>
        public bool Repair { get { return RepairNumber > 0; } set { RepairNumber = value ? Math.Max(1, RepairNumber) : 0; } }
        /// <summary>Correction request number for this step: 0 for the first try, then 1, 2 and so on up to the debate's step retry limit.</summary>
        public int RepairNumber { get; set; }
        /// <summary>Moderator context frozen before invocation.</summary>
        public long ContextEpoch { get; set; }
        /// <summary>Roster revision frozen before invocation.</summary>
        public long RosterRevision { get; set; } = 1;

        /// <summary>Elapsed milliseconds waiting for shared endpoint capacity; zero for immediate admission, CLI or older records.</summary>
        public long EndpointWaitMilliseconds { get; set; }
        private List<CredentialRevision> _CredentialRevisions = new List<CredentialRevision>();
        /// <summary>Credential identities and revisions actually resolved for this invocation; never credential values.</summary>
        public List<CredentialRevision> CredentialRevisions { get { return _CredentialRevisions; } set { System.ArgumentNullException.ThrowIfNull(value); _CredentialRevisions = value; } }
        private List<AttemptResource> _Resources = new List<AttemptResource>();
        /// <summary>Resources this attempt read or wrote through the resource tools, in call order.</summary>
        public List<AttemptResource> Resources { get { return _Resources; } set { System.ArgumentNullException.ThrowIfNull(value); _Resources = value; } }
    }
}
