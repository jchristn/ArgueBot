namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One change to a question request, for inbox badges and notifications. Carries identifiers and status only; interfaces render their own text.</summary>
    public sealed class QuestionEvent
    {
        private string _Cursor = "";
        private string _RequestId = "";
        private string _ConversationId = "";
        private string _Status = "";

        /// <summary>Opaque cursor; pass it as after to read later changes.</summary>
        public string Cursor { get { return _Cursor; } set { ArgumentNullException.ThrowIfNull(value); _Cursor = value; } }

        /// <summary>Question request that changed.</summary>
        public string RequestId { get { return _RequestId; } set { ArgumentNullException.ThrowIfNull(value); _RequestId = value; } }

        /// <summary>Conversation of the request.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        /// <summary>opened, held, parked, answered, skipped, expired or cancelled.</summary>
        public string Status { get { return _Status; } set { ArgumentNullException.ThrowIfNull(value); _Status = value; } }

        /// <summary>When the change happened.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
