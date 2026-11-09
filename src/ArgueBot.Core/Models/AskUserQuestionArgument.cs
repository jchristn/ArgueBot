namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>One question in an ask_user call, as the agent writes it.</summary>
    public sealed class AskUserQuestionArgument
    {
        private string _Key = "";
        private string _Header = "";
        private string _Prompt = "";
        private string _Kind = "";
        private List<AskUserOptionArgument> _Options = new List<AskUserOptionArgument>();
        private string _Placeholder = "";

        /// <summary>Identifier, 1 through 32 characters, unique within the call.</summary>
        public string Key { get { return _Key; } set { ArgumentNullException.ThrowIfNull(value); _Key = value; } }

        /// <summary>Short label, up to 40 characters.</summary>
        public string Header { get { return _Header; } set { ArgumentNullException.ThrowIfNull(value); _Header = value; } }

        /// <summary>The question, up to 1000 characters.</summary>
        public string Prompt { get { return _Prompt; } set { ArgumentNullException.ThrowIfNull(value); _Prompt = value; } }

        /// <summary>text, single_choice, multi_choice or confirm.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        /// <summary>Choices for single_choice and multi_choice.</summary>
        public List<AskUserOptionArgument> Options { get { return _Options; } set { ArgumentNullException.ThrowIfNull(value); _Options = value; } }

        /// <summary>Whether a choice question also accepts free text.</summary>
        public bool AllowOther { get; set; }

        /// <summary>Fewest multi_choice selections.</summary>
        public int MinSelections { get; set; }

        /// <summary>Most multi_choice selections.</summary>
        public int MaxSelections { get; set; }

        /// <summary>Longest text answer.</summary>
        public int MaxLength { get; set; }

        /// <summary>Hint shown in an empty text answer.</summary>
        public string Placeholder { get { return _Placeholder; } set { ArgumentNullException.ThrowIfNull(value); _Placeholder = value; } }

        /// <summary>Whether the question must be answered. Default true.</summary>
        public bool? Required { get; set; }
    }
}
