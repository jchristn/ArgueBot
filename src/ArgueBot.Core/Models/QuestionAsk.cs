namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A validated ask_user call, bound by the service to the invocation that made it. Tool input never chooses the tenant, conversation, run or seat.</summary>
    public sealed class QuestionAsk
    {
        private string _ConversationId = "";
        private string _RunId = "";
        private string _AttemptId = "";
        private string _CallId = "";
        private string _AskerId = "";
        private string _AskerName = "";
        private string _AskerRole = "participant";
        private string _Phase = "";
        private string _Context = "";
        private string _Visibility = "panel";
        private List<Question> _Questions = new List<Question>();

        /// <summary>Conversation of the asking run.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        /// <summary>Asking run.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }

        /// <summary>Generation attempt that made the call.</summary>
        public string AttemptId { get { return _AttemptId; } set { ArgumentNullException.ThrowIfNull(value); _AttemptId = value; } }

        /// <summary>Tool call identifier, unique within the run.</summary>
        public string CallId { get { return _CallId; } set { ArgumentNullException.ThrowIfNull(value); _CallId = value; } }

        /// <summary>Asking seat or orchestrator profile.</summary>
        public string AskerId { get { return _AskerId; } set { ArgumentNullException.ThrowIfNull(value); _AskerId = value; } }

        /// <summary>Display name of the asking seat.</summary>
        public string AskerName { get { return _AskerName; } set { ArgumentNullException.ThrowIfNull(value); _AskerName = value; } }

        /// <summary>participant, orchestrator or followup.</summary>
        public string AskerRole { get { return _AskerRole; } set { ArgumentNullException.ThrowIfNull(value); _AskerRole = value; } }

        /// <summary>Debate phase at the time of the call.</summary>
        public string Phase { get { return _Phase; } set { ArgumentNullException.ThrowIfNull(value); _Phase = value; } }

        /// <summary>Debate round at the time of the call.</summary>
        public long Round { get; set; }

        /// <summary>Why the agent asks, up to 2000 characters.</summary>
        public string Context { get { return _Context; } set { ArgumentNullException.ThrowIfNull(value); _Context = value; } }

        /// <summary>Whether the turn waits for the answer.</summary>
        public bool Wait { get; set; } = true;

        /// <summary>panel or self.</summary>
        public string Visibility { get { return _Visibility; } set { ArgumentNullException.ThrowIfNull(value); _Visibility = value; } }

        /// <summary>When an unanswered request expires; null for never.</summary>
        public DateTime? ExpiresUtc { get; set; }

        /// <summary>Most requests this attempt may make, from the debate policy.</summary>
        public int MaxPerTurn { get; set; } = 2;

        /// <summary>Most requests the debate may make, from the debate policy.</summary>
        public int MaxPerRun { get; set; } = 20;

        /// <summary>Validated questions.</summary>
        public List<Question> Questions { get { return _Questions; } set { ArgumentNullException.ThrowIfNull(value); _Questions = value; } }
    }
}
