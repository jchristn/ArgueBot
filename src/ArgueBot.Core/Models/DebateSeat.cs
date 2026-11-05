namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Historical participant membership in a debate.</summary>
    public sealed class DebateSeat
    {
        private string _ParticipantId = "";
        /// <summary>Immutable runner snapshot ID.</summary>
        public string ParticipantId { get { return _ParticipantId; } set { ArgumentNullException.ThrowIfNull(value); _ParticipantId = value; } }
        private string _Name = "";
        /// <summary>Snapshot display name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }
        /// <summary>Ordered seat position.</summary>
        public int Position { get; set; } = 0;
        /// <summary>Whether the seat currently participates.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Roster revision where the seat joined.</summary>
        public long JoinedRevision { get; set; } = 1;
        /// <summary>Roster revision where the seat left, if any.</summary>
        public long? LeftRevision { get; set; }
    }
}
