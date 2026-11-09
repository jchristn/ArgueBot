namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>The user's answer to one question.</summary>
    public sealed class QuestionAnswer
    {
        private List<string> _Selected = new List<string>();
        private List<string> _SelectedLabels = new List<string>();
        private string _OtherText = "";
        private string _Text = "";
        private string _AnsweredByUserId = "";

        /// <summary>Whether the user left this optional question unanswered, or skipped the whole request.</summary>
        public bool Skipped { get; set; }

        /// <summary>Selected option keys for single-choice and multi-select questions, in the order shown.</summary>
        public List<string> Selected { get { return _Selected; } set { ArgumentNullException.ThrowIfNull(value); _Selected = value; } }

        /// <summary>Labels of the selected options, in the same order as Selected.</summary>
        public List<string> SelectedLabels { get { return _SelectedLabels; } set { ArgumentNullException.ThrowIfNull(value); _SelectedLabels = value; } }

        /// <summary>Free text entered for Other, when the question allows it.</summary>
        public string OtherText { get { return _OtherText; } set { ArgumentNullException.ThrowIfNull(value); _OtherText = value; } }

        /// <summary>Answer text for a text question.</summary>
        public string Text { get { return _Text; } set { ArgumentNullException.ThrowIfNull(value); _Text = value; } }

        /// <summary>Yes (true) or no (false) for a confirm question; null otherwise.</summary>
        public bool? Confirmed { get; set; }

        /// <summary>User who answered.</summary>
        public string AnsweredByUserId { get { return _AnsweredByUserId; } set { ArgumentNullException.ThrowIfNull(value); _AnsweredByUserId = value; } }
    }
}
