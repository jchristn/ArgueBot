namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>The prompt text a debate froze for one step when it started.</summary>
    public sealed class RunPrompt
    {
        private string _Purpose = "";
        /// <summary>Debate step.</summary>
        public string Purpose { get { return _Purpose; } set { ArgumentNullException.ThrowIfNull(value); _Purpose = value; } }

        private string _PromptId = "";
        /// <summary>Source prompt identifier at start time.</summary>
        public string PromptId { get { return _PromptId; } set { ArgumentNullException.ThrowIfNull(value); _PromptId = value; } }

        /// <summary>Source prompt revision at start time.</summary>
        public long PromptRevision { get; set; }

        private string _Name = "";
        /// <summary>Source prompt name at start time.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Body = "";
        /// <summary>Frozen prompt text with placeholders.</summary>
        public string Body { get { return _Body; } set { ArgumentNullException.ThrowIfNull(value); _Body = value; } }
    }
}
