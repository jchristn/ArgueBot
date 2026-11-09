namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>One question within a request the user answers as a unit.</summary>
    public sealed class Question
    {
        private string _Id = "";
        private string _Key = "";
        private string _Header = "";
        private string _Prompt = "";
        private string _Kind = "text";
        private List<QuestionOption> _Options = new List<QuestionOption>();
        private string _Placeholder = "";

        /// <summary>Stable question identifier (qst_...).</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Identifier chosen by the agent, 1 through 32 characters, unique within the request, so it can match answers to questions.</summary>
        public string Key { get { return _Key; } set { ArgumentNullException.ThrowIfNull(value); _Key = value; } }

        /// <summary>Short label, up to 40 characters, used for chips and lists.</summary>
        public string Header { get { return _Header; } set { ArgumentNullException.ThrowIfNull(value); _Header = value; } }

        /// <summary>The question, up to 1000 characters. Agent-written text, shown as data.</summary>
        public string Prompt { get { return _Prompt; } set { ArgumentNullException.ThrowIfNull(value); _Prompt = value; } }

        /// <summary>text, single_choice, multi_choice or confirm.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        /// <summary>Choices for single_choice and multi_choice, 2 through 12; empty for other kinds.</summary>
        public List<QuestionOption> Options { get { return _Options; } set { ArgumentNullException.ThrowIfNull(value); _Options = value; } }

        /// <summary>Whether a choice question also accepts free text as Other.</summary>
        public bool AllowOther { get; set; }

        /// <summary>Fewest options a multi_choice answer selects, 0 through MaxSelections. Default 1 for required questions.</summary>
        public int MinSelections { get; set; }

        /// <summary>Most options a multi_choice answer selects, up to the option count. Default the option count.</summary>
        public int MaxSelections { get; set; }

        /// <summary>Longest text answer in characters, 1 through 8000. Default 8000.</summary>
        public int MaxLength { get; set; }

        /// <summary>Optional hint shown in an empty text answer, up to 120 characters.</summary>
        public string Placeholder { get { return _Placeholder; } set { ArgumentNullException.ThrowIfNull(value); _Placeholder = value; } }

        /// <summary>Whether the question must be answered unless the whole request is skipped.</summary>
        public bool Required { get; set; } = true;

        /// <summary>The answer, once the request is answered or skipped.</summary>
        public QuestionAnswer? Answer { get; set; }
    }
}
