namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One choice offered by a single-choice or multi-select question.</summary>
    public sealed class QuestionOption
    {
        private string _Id = "";
        private string _Key = "";
        private string _Label = "";
        private string _Description = "";

        /// <summary>Stable option identifier (qop_...).</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Option identifier chosen by the agent, 1 through 32 characters, unique within its question. Answers report the selected keys.</summary>
        public string Key { get { return _Key; } set { ArgumentNullException.ThrowIfNull(value); _Key = value; } }

        /// <summary>Option text shown to the user, up to 80 characters.</summary>
        public string Label { get { return _Label; } set { ArgumentNullException.ThrowIfNull(value); _Label = value; } }

        /// <summary>Optional explanation shown under the label, up to 300 characters.</summary>
        public string Description { get { return _Description; } set { ArgumentNullException.ThrowIfNull(value); _Description = value; } }

        /// <summary>Whether the agent recommends this option. At most one option of a single-choice question is recommended.</summary>
        public bool Recommended { get; set; }
    }
}
