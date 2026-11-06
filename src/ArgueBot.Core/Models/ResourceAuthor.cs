namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>The debate agent that is changing a resource. Changes made without an author are attributed to the signed-in user.</summary>
    public sealed class ResourceAuthor
    {
        private string _SeatId = "";
        private string _Name = "";
        private string _RunId = "";
        private string _AttemptId = "";

        /// <summary>Agent seat ID (par_...) in the debate.</summary>
        public string SeatId { get { return _SeatId; } set { ArgumentNullException.ThrowIfNull(value); _SeatId = value; } }

        /// <summary>Agent display name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>Debate run (run_...).</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }

        /// <summary>Generation attempt making the change.</summary>
        public string AttemptId { get { return _AttemptId; } set { ArgumentNullException.ThrowIfNull(value); _AttemptId = value; } }
    }
}
