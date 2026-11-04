namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Durable moderator context with an explicit delivery choice.</summary>
    public sealed class InterjectionRequest
    {
        private string _Text = "";
        /// <summary>Context supplied by the moderator.</summary>
        public string Text { get { return _Text; } set { ArgumentNullException.ThrowIfNull(value); _Text = value; } }
        private string _DeliveryMode = "";
        /// <summary>Required: interrupt_now or next_turn.</summary>
        public string DeliveryMode { get { return _DeliveryMode; } set { ArgumentNullException.ThrowIfNull(value); _DeliveryMode = value; } }
        private string _RecipientId = "";
        /// <summary>Optional active seat ID; empty shares context with the panel.</summary>
        public string RecipientId { get { return _RecipientId; } set { ArgumentNullException.ThrowIfNull(value); _RecipientId = value; } }
        private string _RunId = "";
        /// <summary>Optional target run; empty adds post-run conversation context.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }
        private string _IdempotencyKey = "";
        /// <summary>Unique command key; exact retries return the same receipt.</summary>
        public string IdempotencyKey { get { return _IdempotencyKey; } set { ArgumentNullException.ThrowIfNull(value); _IdempotencyKey = value; } }
        /// <summary>Keep this constraint in subsequent model context.</summary>
        public bool Pinned { get; set; } = false;
        private string _ClarificationId = "";
        /// <summary>Optional clarification being answered or explicitly skipped.</summary>
        public string ClarificationId { get { return _ClarificationId; } set { System.ArgumentNullException.ThrowIfNull(value); _ClarificationId = value; } }
        /// <summary>Record an explicit skip instead of treating the text as a supplied fact.</summary>
        public bool SkipClarification { get; set; }
    }
}
