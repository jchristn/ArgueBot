namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Questions one agent asked the user in a single ask_user call, with their status and resolution.</summary>
    public sealed class QuestionRequest
    {
        private string _TenantId = "";
        private string _OwnerUserId = "";
        private string _Id = "";
        private string _ConversationId = "";
        private string _ConversationTitle = "";
        private string _RunId = "";
        private string _AttemptId = "";
        private string _AskerId = "";
        private string _AskerName = "";
        private string _AskerRole = "participant";
        private string _Phase = "";
        private string _Context = "";
        private string _Visibility = "panel";
        private string _Status = "open";
        private string _HoldState = "none";
        private string _ResolutionReason = "";
        private string _ResolutionNote = "";
        private string _ResolvedByUserId = "";
        private string _AnswerMessageId = "";
        private string _ResumeAttemptId = "";
        private string _RunState = "";
        private List<Question> _Questions = new List<Question>();

        /// <summary>Tenant of the request. Service-only; not part of API responses.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        /// <summary>User who created the conversation. Service-only; not part of API responses.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string OwnerUserId { get { return _OwnerUserId; } set { ArgumentNullException.ThrowIfNull(value); _OwnerUserId = value; } }

        /// <summary>Request identifier (qrq_...).</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Conversation the request belongs to.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        /// <summary>Conversation title, for inbox lists.</summary>
        public string ConversationTitle { get { return _ConversationTitle; } set { ArgumentNullException.ThrowIfNull(value); _ConversationTitle = value; } }

        /// <summary>Run whose agent asked.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }

        /// <summary>Generation attempt that asked.</summary>
        public string AttemptId { get { return _AttemptId; } set { ArgumentNullException.ThrowIfNull(value); _AttemptId = value; } }

        /// <summary>Seat or orchestrator profile that asked.</summary>
        public string AskerId { get { return _AskerId; } set { ArgumentNullException.ThrowIfNull(value); _AskerId = value; } }

        /// <summary>Display name of the asking seat.</summary>
        public string AskerName { get { return _AskerName; } set { ArgumentNullException.ThrowIfNull(value); _AskerName = value; } }

        /// <summary>participant, orchestrator or followup.</summary>
        public string AskerRole { get { return _AskerRole; } set { ArgumentNullException.ThrowIfNull(value); _AskerRole = value; } }

        /// <summary>Debate phase when the agent asked, such as intake, question or response.</summary>
        public string Phase { get { return _Phase; } set { ArgumentNullException.ThrowIfNull(value); _Phase = value; } }

        /// <summary>Debate round when the agent asked.</summary>
        public long Round { get; set; }

        /// <summary>Why the agent is asking, up to 2000 characters of markdown. Agent-written text, shown as data.</summary>
        public string Context { get { return _Context; } set { ArgumentNullException.ThrowIfNull(value); _Context = value; } }

        /// <summary>Whether the turn waits for the answer. A request that does not wait delivers its answer to the asking seat's next turn.</summary>
        public bool Wait { get; set; } = true;

        /// <summary>panel (every seat sees the answer) or self (only the asking seat).</summary>
        public string Visibility { get { return _Visibility; } set { ArgumentNullException.ThrowIfNull(value); _Visibility = value; } }

        /// <summary>open, answered, skipped, expired or cancelled.</summary>
        public string Status { get { return _Status; } set { ArgumentNullException.ThrowIfNull(value); _Status = value; } }

        /// <summary>held (a live tool call is waiting), parked (the turn ended; a resume attempt follows the answer) or none.</summary>
        public string HoldState { get { return _HoldState; } set { ArgumentNullException.ThrowIfNull(value); _HoldState = value; } }

        /// <summary>For a skipped request: skipped_by_user or expired. For a cancelled request: run_stopped.</summary>
        public string ResolutionReason { get { return _ResolutionReason; } set { ArgumentNullException.ThrowIfNull(value); _ResolutionReason = value; } }

        /// <summary>Optional note the user added when skipping.</summary>
        public string ResolutionNote { get { return _ResolutionNote; } set { ArgumentNullException.ThrowIfNull(value); _ResolutionNote = value; } }

        /// <summary>User who answered or skipped.</summary>
        public string ResolvedByUserId { get { return _ResolvedByUserId; } set { ArgumentNullException.ThrowIfNull(value); _ResolvedByUserId = value; } }

        /// <summary>Transcript message that carries the answer to the panel.</summary>
        public string AnswerMessageId { get { return _AnswerMessageId; } set { ArgumentNullException.ThrowIfNull(value); _AnswerMessageId = value; } }

        /// <summary>Attempt that resumed the parked turn with the answer.</summary>
        public string ResumeAttemptId { get { return _ResumeAttemptId; } set { ArgumentNullException.ThrowIfNull(value); _ResumeAttemptId = value; } }

        /// <summary>Whether the question text looks like a request for a password, token or key. Interfaces show a stronger warning.</summary>
        public bool CredentialWarning { get; set; }

        /// <summary>When an unanswered request expires; null when it never expires.</summary>
        public DateTime? ExpiresUtc { get; set; }

        /// <summary>When the request was resolved.</summary>
        public DateTime? ResolvedUtc { get; set; }

        /// <summary>When the agent asked.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>State of the debate run that asked, such as awaiting_input or completed.</summary>
        public string RunState { get { return _RunState; } set { ArgumentNullException.ThrowIfNull(value); _RunState = value; } }

        /// <summary>Whether the request can be deleted: it is resolved and its debate has finished, so no turn or later prompt still depends on it.</summary>
        public bool Deletable { get { return _Status != "open" && (_RunState == "completed" || _RunState == "cancelled" || _RunState == "failed"); } }

        /// <summary>One through four questions, in order.</summary>
        public List<Question> Questions { get { return _Questions; } set { ArgumentNullException.ThrowIfNull(value); _Questions = value; } }
    }
}
