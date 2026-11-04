namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Validated public output from a question, synthesis or compaction invocation.</summary>
    public sealed class OrchestrationResponse
    {
        private string _Question = "";
        /// <summary>Pointed question for the panel.</summary>
        public string Question { get { return _Question; } set { ArgumentNullException.ThrowIfNull(value); _Question = value; } }
        private string _Rationale = "";
        /// <summary>Brief public explanation, without hidden reasoning.</summary>
        public string Rationale { get { return _Rationale; } set { ArgumentNullException.ThrowIfNull(value); _Rationale = value; } }
        private string _Proposal = "";
        /// <summary>Exact candidate proposal to be ratified.</summary>
        public string Proposal { get { return _Proposal; } set { ArgumentNullException.ThrowIfNull(value); _Proposal = value; } }
        private string _Summary = "";
        /// <summary>Source-grounded context summary.</summary>
        public string Summary { get { return _Summary; } set { ArgumentNullException.ThrowIfNull(value); _Summary = value; } }
        private List<string> _SourceIds = new List<string>();
        /// <summary>Original sources cited by the summary.</summary>
        public List<string> SourceIds { get { return _SourceIds; } set { ArgumentNullException.ThrowIfNull(value); _SourceIds = value; } }
        private List<SummarySection> _Sections = new List<SummarySection>();
        /// <summary>Explicit fact, dispute, position, evidence, decision, question and constraint sections for compaction.</summary>
        public List<SummarySection> Sections { get { return _Sections; } set { ArgumentNullException.ThrowIfNull(value); _Sections = value; } }
    }
}
