namespace ArgueBot.Core.Models
{
    /// <summary>Bounded selectors accepted by conversation retrieval and clarification tools.</summary>
    public sealed class ConversationToolArguments
    {
        private string _Id = "";
        /// <summary>Original turn or summary ID, when required.</summary>
        public string Id { get { return _Id; } set { System.ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        private string _Query = "";
        /// <summary>Search text or a clarification question.</summary>
        public string Query { get { return _Query; } set { System.ArgumentNullException.ThrowIfNull(value); _Query = value; } }
        /// <summary>Exclusive source sequence cursor.</summary>
        public long After { get; set; }
        /// <summary>Maximum results, one through twenty.</summary>
        public int Limit { get; set; } = 10;
        /// <summary>Content offset for original turns, or a multiple of 100 selecting a summary source page.</summary>
        public int Offset { get; set; }
        /// <summary>UTF-16 offset into summary text; requires a summary ID when nonzero.</summary>
        public int ContentOffset { get; set; }
        /// <summary>Whether clarification must pause the debate for a moderator answer.</summary>
        public bool Blocking { get; set; }
        /// <summary>Requested original-turn or summary excerpt length, 1 through 8192 UTF-16 characters.</summary>
        public int Length { get; set; } = 2048;
        private System.Collections.Generic.List<string> _Ids = new System.Collections.Generic.List<string>();
        /// <summary>Up to twenty turn or summary IDs whose supporting evidence to read.</summary>
        public System.Collections.Generic.List<string> Ids { get { return _Ids; } set { System.ArgumentNullException.ThrowIfNull(value); _Ids = value; } }
        private string _SeatId = "";
        /// <summary>Only turns written by this seat of the current run.</summary>
        public string SeatId { get { return _SeatId; } set { System.ArgumentNullException.ThrowIfNull(value); _SeatId = value; } }
        private string _Phase = "";
        /// <summary>Only turns from this phase: question, response, synthesis, ratification, compaction, or moderator for moderator-written context.</summary>
        public string Phase { get { return _Phase; } set { System.ArgumentNullException.ThrowIfNull(value); _Phase = value; } }
    }
}
