namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>CommandRequest transport and domain data.</summary>
    public sealed class CommandRequest
    {
        /// <summary>Requested operation.</summary>
        public string Action { get; set; } = "";

        /// <summary>User text.</summary>
        public string Text { get; set; } = "";

        /// <summary>Target profile.</summary>
        public string? RecipientId { get; set; }

        /// <summary>Unique command key.</summary>
        public string IdempotencyKey { get; set; } = "";

        /// <summary>Expected resource revision.</summary>
        public long? Revision { get; set; }

        /// <summary>Extension rounds.</summary>
        public int Rounds { get; set; } = 2;

    }
}
