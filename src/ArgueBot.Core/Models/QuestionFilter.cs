namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Selects question requests for the inbox and run views.</summary>
    public sealed class QuestionFilter
    {
        private string _Status = "";
        private string _ConversationId = "";
        private string _RunId = "";
        private string _Scope = "mine";

        /// <summary>open, answered, skipped, expired, cancelled, resolved (all but open) or empty for all.</summary>
        public string Status { get { return _Status; } set { ArgumentNullException.ThrowIfNull(value); _Status = value; } }

        /// <summary>Only requests in this conversation.</summary>
        public string ConversationId { get { return _ConversationId; } set { ArgumentNullException.ThrowIfNull(value); _ConversationId = value; } }

        /// <summary>Only requests in this run.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }

        /// <summary>mine (conversations the caller created) or tenant (every conversation in the tenant).</summary>
        public string Scope { get { return _Scope; } set { ArgumentNullException.ThrowIfNull(value); _Scope = value; } }

        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Requests per page, 1 through 100.</summary>
        public int PageSize { get; set; } = 25;
    }
}
