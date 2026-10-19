namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Page transport and domain data.</summary>
    public sealed class Page
    {
        /// <summary>Matching conversations.</summary>
        public List<Conversation> Items { get; set; } = new List<Conversation>();

        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;

        /// <summary>Total matches.</summary>
        public long TotalCount { get; set; }

    }
}
