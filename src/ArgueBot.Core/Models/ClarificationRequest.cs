namespace ArgueBot.Core.Models
{
    /// <summary>Durable request for facts from the moderator.</summary>
    public sealed class ClarificationRequest
    {
        /// <summary>Request ID.</summary>
        public string Id { get; set; } = "";
        /// <summary>Originating seat.</summary>
        public string ParticipantId { get; set; } = "";
        /// <summary>Question the moderator should answer.</summary>
        public string Question { get; set; } = "";
        /// <summary>Whether execution pauses for the answer.</summary>
        public bool Blocking { get; set; }
        /// <summary>open, answered or skipped.</summary>
        public string Status { get; set; } = "open";
    }
}
