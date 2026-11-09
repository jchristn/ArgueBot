namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Arguments of the ask_user conversation tool.</summary>
    public sealed class AskUserArguments
    {
        private string _Context = "";
        private List<AskUserQuestionArgument> _Questions = new List<AskUserQuestionArgument>();
        private string _Visibility = "";

        /// <summary>Why the agent is asking, up to 2000 characters.</summary>
        public string Context { get { return _Context; } set { ArgumentNullException.ThrowIfNull(value); _Context = value; } }

        /// <summary>One through four questions.</summary>
        public List<AskUserQuestionArgument> Questions { get { return _Questions; } set { ArgumentNullException.ThrowIfNull(value); _Questions = value; } }

        /// <summary>Whether the turn waits for the answer. Default true.</summary>
        public bool? Wait { get; set; }

        /// <summary>panel (default) or self.</summary>
        public string Visibility { get { return _Visibility; } set { ArgumentNullException.ThrowIfNull(value); _Visibility = value; } }
    }
}
