namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>AttemptPage operational contract.</summary>
    public sealed class AttemptPage
    {
        private List<GenerationAttempt> _Items = new List<GenerationAttempt>();

        /// <summary>Matching durable attempts.</summary>
        public List<GenerationAttempt> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Total matching records.</summary>
        public long TotalCount { get; set; }
    }
}
