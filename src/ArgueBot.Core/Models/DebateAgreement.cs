namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>One immutable seat position on a specific proposal.</summary>
    public sealed class DebateAgreement
    {
        private string _ParticipantId = "";
        /// <summary>Stable participant snapshot ID.</summary>
        public string ParticipantId { get { return _ParticipantId; } set { ArgumentNullException.ThrowIfNull(value); _ParticipantId = value; } }
        private string _MessageId = "";
        /// <summary>Original ratification response.</summary>
        public string MessageId { get { return _MessageId; } set { ArgumentNullException.ThrowIfNull(value); _MessageId = value; } }
        private string _Position = "";
        /// <summary>Explicit agreement, disagreement, abstention or information request.</summary>
        public string Position { get { return _Position; } set { ArgumentNullException.ThrowIfNull(value); _Position = value; } }
        private string _Rationale = "";
        /// <summary>Public explanation.</summary>
        public string Rationale { get { return _Rationale; } set { ArgumentNullException.ThrowIfNull(value); _Rationale = value; } }
        /// <summary>True when the same seat later voted again on this proposal; only the newest vote counts toward unanimity.</summary>
        public bool Superseded { get; set; }
    }
}
