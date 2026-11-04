namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Boundary-applied change to the active debate roster.</summary>
    public sealed class RosterChangeRequest
    {
        private string _Action = "";
        /// <summary>add, remove or replace.</summary>
        public string Action { get { return _Action; } set { ArgumentNullException.ThrowIfNull(value); _Action = value; } }
        private string _ParticipantId = "";
        /// <summary>Existing seat for remove/replace.</summary>
        public string ParticipantId { get { return _ParticipantId; } set { ArgumentNullException.ThrowIfNull(value); _ParticipantId = value; } }
        private string _ProfileId = "";
        /// <summary>Reusable profile to snapshot for add/replace.</summary>
        public string ProfileId { get { return _ProfileId; } set { ArgumentNullException.ThrowIfNull(value); _ProfileId = value; } }
        private string _IdempotencyKey = "";
        /// <summary>Unique command key.</summary>
        public string IdempotencyKey { get { return _IdempotencyKey; } set { ArgumentNullException.ThrowIfNull(value); _IdempotencyKey = value; } }
        /// <summary>Expected run revision.</summary>
        public long Revision { get; set; } = 0;
    }
}
