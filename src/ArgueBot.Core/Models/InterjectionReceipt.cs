namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Accepted moderator context and its recorded delivery mode.</summary>
    public sealed class InterjectionReceipt
    {
        private string _Id = "";
        /// <summary>Receipt identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        private string _MessageId = "";
        /// <summary>Original moderator message.</summary>
        public string MessageId { get { return _MessageId; } set { ArgumentNullException.ThrowIfNull(value); _MessageId = value; } }
        private string _RunId = "";
        /// <summary>Target run if specified.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }
        private string _DeliveryMode = "";
        /// <summary>The moderator-selected mode.</summary>
        public string DeliveryMode { get { return _DeliveryMode; } set { ArgumentNullException.ThrowIfNull(value); _DeliveryMode = value; } }
        /// <summary>Epoch after acceptance.</summary>
        public long ContextEpoch { get; set; } = 0;
        private string _Status = "queued";
        /// <summary>queued, interrupt_requested, applied, summarized or post_run. Delivery counts distinguish original context from derived memory.</summary>
        public string Status { get { return _Status; } set { ArgumentNullException.ThrowIfNull(value); _Status = value; } }
        /// <summary>Whether the original constraint is pinned.</summary>
        public bool Pinned { get; set; } = false;
        private List<string> _DeliveredParticipantIds = new List<string>();
        /// <summary>Seats whose successful invocation included this original context.</summary>
        public List<string> DeliveredParticipantIds { get { return _DeliveredParticipantIds; } set { System.ArgumentNullException.ThrowIfNull(value); _DeliveredParticipantIds = value; } }
        private List<string> _SummarizedParticipantIds = new List<string>();
        /// <summary>Seats whose successful invocation selected a summary citing this source. This does not prove faithful preservation or include those already listed under direct delivery.</summary>
        public List<string> SummarizedParticipantIds { get { return _SummarizedParticipantIds; } set { ArgumentNullException.ThrowIfNull(value); _SummarizedParticipantIds = value; } }
        internal bool WasDuplicate { get; set; }
    }
}
