namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>The answer to one question in an answer request.</summary>
    public sealed class QuestionAnswerInput
    {
        private string _QuestionId = "";
        private string _Key = "";
        private List<string> _Selected = new List<string>();
        private string _OtherText = "";
        private string _Text = "";

        /// <summary>Question identifier (qst_...). Either QuestionId or Key is required.</summary>
        public string QuestionId { get { return _QuestionId; } set { ArgumentNullException.ThrowIfNull(value); _QuestionId = value; } }

        /// <summary>Question key chosen by the agent. Either QuestionId or Key is required.</summary>
        public string Key { get { return _Key; } set { ArgumentNullException.ThrowIfNull(value); _Key = value; } }

        /// <summary>Leave this optional question unanswered.</summary>
        public bool Skip { get; set; }

        /// <summary>Selected option keys for single_choice (exactly one, unless Other is used) and multi_choice.</summary>
        public List<string> Selected { get { return _Selected; } set { ArgumentNullException.ThrowIfNull(value); _Selected = value; } }

        /// <summary>Free text for Other, when the question allows it, up to 2000 characters.</summary>
        public string OtherText { get { return _OtherText; } set { ArgumentNullException.ThrowIfNull(value); _OtherText = value; } }

        /// <summary>Answer for a text question, up to the question's MaxLength.</summary>
        public string Text { get { return _Text; } set { ArgumentNullException.ThrowIfNull(value); _Text = value; } }

        /// <summary>Yes or no for a confirm question.</summary>
        public bool? Confirmed { get; set; }
    }
}
