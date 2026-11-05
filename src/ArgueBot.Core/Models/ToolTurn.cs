namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One visible turn as conversation tools describe it, with a bounded excerpt.</summary>
    public sealed class ToolTurn
    {
        private string _Id = "";
        private string _Speaker = "";
        private string _Kind = "";
        private string _SeatId = "";
        private string _Phase = "";
        private string _Snippet = "";
        /// <summary>Original turn ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        /// <summary>Conversation sequence number.</summary>
        public long Sequence { get; set; }
        /// <summary>Speaker name; user means the moderator.</summary>
        public string Speaker { get { return _Speaker; } set { ArgumentNullException.ThrowIfNull(value); _Speaker = value; } }
        /// <summary>Message kind.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }
        /// <summary>Seat that produced the turn, or empty for moderator context.</summary>
        public string SeatId { get { return _SeatId; } set { ArgumentNullException.ThrowIfNull(value); _SeatId = value; } }
        /// <summary>Debate phase that produced the turn, moderator for moderator context, or empty.</summary>
        public string Phase { get { return _Phase; } set { ArgumentNullException.ThrowIfNull(value); _Phase = value; } }
        /// <summary>Preview, or an excerpt centered on the search match.</summary>
        public string Snippet { get { return _Snippet; } set { ArgumentNullException.ThrowIfNull(value); _Snippet = value; } }
        /// <summary>UTF-16 offset of the first match, or null when not searching.</summary>
        public int? MatchOffset { get; set; }
        /// <summary>Total characters in the original turn.</summary>
        public long Characters { get; set; }
        /// <summary>True when the snippet is shorter than the original; read it with get_turn.</summary>
        public bool Truncated { get; set; }
    }
}
