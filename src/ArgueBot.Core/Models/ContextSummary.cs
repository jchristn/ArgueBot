namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Model-written context with immutable original-source provenance.</summary>
    public sealed class ContextSummary
    {
        private string _Id = "";
        /// <summary>Stable summary identifier.</summary>
        public string Id { get { return _Id; } set { System.ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        private string _MessageId = "";
        /// <summary>Original model response containing the summary.</summary>
        public string MessageId { get { return _MessageId; } set { System.ArgumentNullException.ThrowIfNull(value); _MessageId = value; } }
        private string _AttemptId = "";
        /// <summary>Generation attempt with the model snapshot and prompt digest.</summary>
        public string AttemptId { get { return _AttemptId; } set { System.ArgumentNullException.ThrowIfNull(value); _AttemptId = value; } }
        private string _RunId = "";
        /// <summary>Containing run.</summary>
        public string RunId { get { return _RunId; } set { System.ArgumentNullException.ThrowIfNull(value); _RunId = value; } }
        private string _Content = "";
        /// <summary>Summary text; original turns remain authoritative.</summary>
        public string Content { get { return _Content; } set { System.ArgumentNullException.ThrowIfNull(value); _Content = value; } }
        /// <summary>Last original conversation sequence covered.</summary>
        public long ThroughSequence { get; set; }
        /// <summary>Context epoch at generation acceptance.</summary>
        public long ContextEpoch { get; set; }
        /// <summary>Whether this summary is eligible for prompt selection.</summary>
        public bool Active { get; set; }
        private string _RecipientId = "";
        /// <summary>Private recipient snapshot ID; empty denotes shared memory.</summary>
        public string RecipientId { get { return _RecipientId; } set { System.ArgumentNullException.ThrowIfNull(value); _RecipientId = value; } }
        /// <summary>Total original source identities in this immutable manifest.</summary>
        public long SourceCount { get; set; }
        /// <summary>One-based page of source identities returned, 100 per page.</summary>
        public int SourcePageNumber { get; set; } = 1;
        private List<string> _SourceIds = new List<string>();
        /// <summary>One bounded page of original source IDs, retained through successive compactions.</summary>
        public List<string> SourceIds { get { return _SourceIds; } set { System.ArgumentNullException.ThrowIfNull(value); _SourceIds = value; } }
        private List<SummarySource> _Sources = new List<SummarySource>();
        /// <summary>Content hashes for the same bounded page as SourceIds; hashes establish provenance, not semantic accuracy.</summary>
        public List<SummarySource> Sources { get { return _Sources; } set { ArgumentNullException.ThrowIfNull(value); _Sources = value; } }
        private List<string> _ParentSummaryIds = new List<string>();
        /// <summary>Prior summaries selected for this compaction, ordered by the invocation manifest; originals remain in Sources.</summary>
        public List<string> ParentSummaryIds { get { return _ParentSummaryIds; } set { ArgumentNullException.ThrowIfNull(value); _ParentSummaryIds = value; } }
        private List<SummarySection> _Sections = new List<SummarySection>();
        /// <summary>Seven validated memory categories, also rendered in Content; empty for historical unstructured summaries. Validation does not prove semantic accuracy.</summary>
        public List<SummarySection> Sections { get { return _Sections; } set { ArgumentNullException.ThrowIfNull(value); _Sections = value; } }
    }
}
