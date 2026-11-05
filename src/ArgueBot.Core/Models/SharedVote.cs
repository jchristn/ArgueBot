namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One agent's position on a proposal.</summary>
    public sealed class SharedVote
    {
        private string _ParticipantId = "";
        /// <summary>Seat ID of the voter.</summary>
        public string ParticipantId { get { return _ParticipantId; } set { ArgumentNullException.ThrowIfNull(value); _ParticipantId = value; } }

        private string _MessageId = "";
        /// <summary>Turn that holds the vote.</summary>
        public string MessageId { get { return _MessageId; } set { ArgumentNullException.ThrowIfNull(value); _MessageId = value; } }

        private string _Position = "";
        /// <summary>agree, disagree, abstain or needs_information.</summary>
        public string Position { get { return _Position; } set { ArgumentNullException.ThrowIfNull(value); _Position = value; } }

        private string _Rationale = "";
        /// <summary>The agent's reason.</summary>
        public string Rationale { get { return _Rationale; } set { ArgumentNullException.ThrowIfNull(value); _Rationale = value; } }

        /// <summary>True when a later vote replaced it.</summary>
        public bool Superseded { get; set; }
    }
}
