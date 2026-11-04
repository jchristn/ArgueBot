namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Explicit position on the proposal and context supplied to one seat.</summary>
    public sealed class AgreementResponse
    {
        private string _ProposalId = "";
        /// <summary>Exact proposal identifier supplied by the service.</summary>
        public string ProposalId { get { return _ProposalId; } set { ArgumentNullException.ThrowIfNull(value); _ProposalId = value; } }
        private string _Position = "";
        /// <summary>agree, disagree, abstain or needs_information.</summary>
        public string Position { get { return _Position; } set { ArgumentNullException.ThrowIfNull(value); _Position = value; } }
        private string _Rationale = "";
        /// <summary>Brief explanation of the position.</summary>
        public string Rationale { get { return _Rationale; } set { ArgumentNullException.ThrowIfNull(value); _Rationale = value; } }
    }
}
