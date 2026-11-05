namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A proposed answer and its votes.</summary>
    public sealed class SharedProposal
    {
        private string _Id = "";
        /// <summary>Proposal ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _RunId = "";
        /// <summary>Debate.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }

        private string _Content = "";
        /// <summary>Proposed answer.</summary>
        public string Content { get { return _Content; } set { ArgumentNullException.ThrowIfNull(value); _Content = value; } }

        /// <summary>True when a later proposal replaced it.</summary>
        public bool Superseded { get; set; }

        private List<SharedVote> _Agreements = new List<SharedVote>();
        /// <summary>Votes.</summary>
        public List<SharedVote> Agreements { get { return _Agreements; } set { ArgumentNullException.ThrowIfNull(value); _Agreements = value; } }
    }
}
