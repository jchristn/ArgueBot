namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>The stored request for an ask_user call, and whether an identical open request already existed for the same attempt.</summary>
    public sealed class QuestionAskResult
    {
        private QuestionRequest _Request = new QuestionRequest();

        /// <summary>The stored request.</summary>
        public QuestionRequest Request { get { return _Request; } set { ArgumentNullException.ThrowIfNull(value); _Request = value; } }

        /// <summary>True when a harness repeated the call and the existing request was returned instead of a second one.</summary>
        public bool Existing { get; set; }
    }
}
