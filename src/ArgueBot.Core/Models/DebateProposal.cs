namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Immutable proposal with the roster and context it covers.</summary>
    public sealed class DebateProposal
    {
        private string _Id = "";
        /// <summary>Stable proposal identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        private string _RunId = "";
        /// <summary>Containing run.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }
        private string _MessageId = "";
        /// <summary>Original synthesis message.</summary>
        public string MessageId { get { return _MessageId; } set { ArgumentNullException.ThrowIfNull(value); _MessageId = value; } }
        private string _Content = "";
        /// <summary>Exact candidate text.</summary>
        public string Content { get { return _Content; } set { ArgumentNullException.ThrowIfNull(value); _Content = value; } }
        private string _Digest = "";
        /// <summary>SHA-256 digest of the candidate.</summary>
        public string Digest { get { return _Digest; } set { ArgumentNullException.ThrowIfNull(value); _Digest = value; } }
        /// <summary>Context revision at proposal acceptance.</summary>
        public long ContextEpoch { get; set; } = 0;
        /// <summary>Roster revision at proposal acceptance.</summary>
        public long RosterRevision { get; set; } = 0;
        /// <summary>True when a moderator edit replaced this proposal. Its votes stay as history and never count toward the replacement.</summary>
        public bool Superseded { get; set; }
        /// <summary>True when the moderator wrote this revision instead of the orchestrator.</summary>
        public bool ModeratorEdited { get; set; }
        /// <summary>The proposal this moderator revision replaced, or empty for an orchestrator proposal.</summary>
        public string SupersedesId { get { return _SupersedesId; } set { ArgumentNullException.ThrowIfNull(value); _SupersedesId = value; } }
        private string _SupersedesId = "";
        private List<DebateAgreement> _Agreements = new List<DebateAgreement>();
        /// <summary>Explicit positions on this proposal.</summary>
        public List<DebateAgreement> Agreements { get { return _Agreements; } set { ArgumentNullException.ThrowIfNull(value); _Agreements = value; } }
    }
}
