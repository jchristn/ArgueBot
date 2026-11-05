namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A debate as a public reader sees it. Runner settings, prompts and budgets are not included.</summary>
    public sealed class SharedRun
    {
        private string _Id = "";
        /// <summary>Run ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _State = "";
        /// <summary>Run state.</summary>
        public string State { get { return _State; } set { ArgumentNullException.ThrowIfNull(value); _State = value; } }

        private string _Outcome = "";
        /// <summary>Outcome, when finished.</summary>
        public string Outcome { get { return _Outcome; } set { ArgumentNullException.ThrowIfNull(value); _Outcome = value; } }

        /// <summary>Current or final round.</summary>
        public int Round { get; set; }

        /// <summary>Round limit.</summary>
        public int MaxRounds { get; set; }

        private string _Question = "";
        /// <summary>The question the debate answered.</summary>
        public string Question { get { return _Question; } set { ArgumentNullException.ThrowIfNull(value); _Question = value; } }

        /// <summary>1 for a fixed panel, 2 for an orchestrated debate.</summary>
        public int ProtocolVersion { get; set; }

        /// <summary>When the debate started.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Orchestrator, for protocol 2.</summary>
        public SharedSeat? Orchestrator { get; set; }

        private List<SharedSeat> _Participants = new List<SharedSeat>();
        /// <summary>Debating agents.</summary>
        public List<SharedSeat> Participants { get { return _Participants; } set { ArgumentNullException.ThrowIfNull(value); _Participants = value; } }
    }
}
