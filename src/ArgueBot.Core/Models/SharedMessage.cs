namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A transcript turn as a public reader sees it.</summary>
    public sealed class SharedMessage
    {
        private string _Id = "";
        /// <summary>Message ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _RunId = "";
        /// <summary>Debate the turn belongs to, or empty for a moderator note.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }

        private string _Speaker = "";
        /// <summary>Speaker name, or user for the moderator.</summary>
        public string Speaker { get { return _Speaker; } set { ArgumentNullException.ThrowIfNull(value); _Speaker = value; } }

        private string _Kind = "";
        /// <summary>Turn kind, for example question, answer, proposal, ratification or tool.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }

        private string _State = "";
        /// <summary>completed, failed or interrupted.</summary>
        public string State { get { return _State; } set { ArgumentNullException.ThrowIfNull(value); _State = value; } }

        private string _Content = "";
        /// <summary>Complete text.</summary>
        public string Content { get { return _Content; } set { ArgumentNullException.ThrowIfNull(value); _Content = value; } }

        /// <summary>When the turn was written.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
