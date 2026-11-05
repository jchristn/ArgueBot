namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>A bounded page of visible turns returned by conversation tools.</summary>
    public sealed class ToolPage
    {
        /// <summary>Turns in sequence order.</summary>
        public List<ToolTurn> Items { get; } = new List<ToolTurn>();
        /// <summary>Pass as after for the next page; null on the last page.</summary>
        public long? NextAfter { get; set; }
        /// <summary>True when a search stopped at its scan bound before reaching the end; continue from nextAfter.</summary>
        public bool Partial { get; set; }
    }
}
