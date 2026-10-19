namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>HistoryFilter operational contract.</summary>
    public sealed class HistoryFilter
    {
        private string _Search = "";
        private string _Method = "";

        /// <summary>Path search.</summary>
        public string Search { get { return _Search; } set { ArgumentNullException.ThrowIfNull(value); _Search = value; } }
        /// <summary>Optional HTTP method.</summary>
        public string Method { get { return _Method; } set { ArgumentNullException.ThrowIfNull(value); _Method = value; } }
        /// <summary>Optional exact response status.</summary>
        public int? Status { get; set; }
        /// <summary>Optional start UTC.</summary>
        public DateTime? FromUtc { get; set; }
        /// <summary>Optional end UTC.</summary>
        public DateTime? ToUtc { get; set; }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Page size, 1 to 100.</summary>
        public int PageSize { get; set; } = 25;
    }
}
