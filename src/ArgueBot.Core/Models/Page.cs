namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Page transport and domain data.</summary>
    public sealed class Page
    {
        private List<Conversation> _Items = new List<Conversation>();

        /// <summary>Matching conversations.</summary>
        public List<Conversation> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }

        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;

        /// <summary>Total matches.</summary>
        public long TotalCount { get; set; }

    }
}
