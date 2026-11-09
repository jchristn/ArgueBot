namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A page of question requests, newest first, with the exact total.</summary>
    public sealed class QuestionPage
    {
        private List<QuestionRequest> _Items = new List<QuestionRequest>();

        /// <summary>Requests on this page.</summary>
        public List<QuestionRequest> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }

        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Requests per page.</summary>
        public int PageSize { get; set; } = 25;

        /// <summary>Requests matching the filter.</summary>
        public long TotalCount { get; set; }
    }
}
