namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Conversation transport and domain data.</summary>
    public sealed class Conversation
    {
        /// <summary>Stable conversation identifier.</summary>
        public string Id { get; set; } = "";

        /// <summary>Owning tenant.</summary>
        public string TenantId { get; set; } = "";

        /// <summary>Owning user.</summary>
        public string UserId { get; set; } = "";

        /// <summary>Display title.</summary>
        public string Title { get; set; } = "";

        /// <summary>Whether pinned.</summary>
        public bool Pinned { get; set; }

        /// <summary>Whether archived.</summary>
        public bool Archived { get; set; }

        /// <summary>Optimistic revision.</summary>
        public long Revision { get; set; }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Last change time in UTC.</summary>
        public DateTime UpdatedUtc { get; set; }

    }
}
