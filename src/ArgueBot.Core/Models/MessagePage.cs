namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>MessagePage operational contract.</summary>
    public sealed class MessagePage
    {
        private List<Message> _Items = new List<Message>();

        /// <summary>Ordered bounded message previews.</summary>
        public List<Message> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total matching records.</summary>
        public long TotalCount { get; set; }
        /// <summary>Membership boundary captured for this page. Reuse it to traverse a stable set while new messages arrive; streaming content itself can still advance.</summary>
        public long ThroughSequence { get; set; }
        /// <summary>Latest stored sequence at request start, including messages beyond a supplied membership boundary.</summary>
        public long LatestSequence { get; set; }
    }
}
