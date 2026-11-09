namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Question changes after a cursor, with the cursor to continue from.</summary>
    public sealed class QuestionEventPage
    {
        private List<QuestionEvent> _Items = new List<QuestionEvent>();
        private string _Next = "";

        /// <summary>Changes, oldest first.</summary>
        public List<QuestionEvent> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }

        /// <summary>Cursor to pass as after for the next read. Equal to the request's cursor when nothing changed.</summary>
        public string Next { get { return _Next; } set { ArgumentNullException.ThrowIfNull(value); _Next = value; } }
    }
}
