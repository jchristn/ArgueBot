namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>CommandRequest transport and domain data.</summary>
    public sealed class CommandRequest
    {
        private string _Action = "";
        private string _Text = "";
        private string _IdempotencyKey = "";

        /// <summary>Requested operation.</summary>
        public string Action { get { return _Action; } set { ArgumentNullException.ThrowIfNull(value); _Action = value; } }

        /// <summary>User text.</summary>
        public string Text { get { return _Text; } set { ArgumentNullException.ThrowIfNull(value); _Text = value; } }

        /// <summary>Target profile.</summary>
        public string? RecipientId { get; set; }

        /// <summary>Unique command key, 1 to 200 characters for durable controls and messages; hold/release are transient.</summary>
        public string IdempotencyKey { get { return _IdempotencyKey; } set { ArgumentNullException.ThrowIfNull(value); _IdempotencyKey = value; } }

        /// <summary>Expected resource revision.</summary>
        public long? Revision { get; set; }

        /// <summary>Extension rounds.</summary>
        public int Rounds { get; set; } = 2;

    }
}
