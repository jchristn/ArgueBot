namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;
    using System;
    using System.Text.Json.Serialization;

    /// <summary>Conversation transport and domain data.</summary>
    public sealed class Conversation
    {
        private string _Id = "";
        private string _TenantId = "";
        private string _UserId = "";
        private string _Title = "";

        /// <summary>Stable conversation identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Latest committed event sequence observed before this conversation read. Capture it before loading snapshots, then replay after it to cover concurrent changes.</summary>
        public long LastEventSequence { get; set; }

        /// <summary>Owning tenant.</summary>
        [JsonIgnore]
        public string TenantId { get { return _TenantId; } set { ArgumentNullException.ThrowIfNull(value); _TenantId = value; } }

        /// <summary>Owning user.</summary>
        [JsonIgnore]
        public string UserId { get { return _UserId; } set { ArgumentNullException.ThrowIfNull(value); _UserId = value; } }

        /// <summary>Display title.</summary>
        public string Title { get { return _Title; } set { ArgumentNullException.ThrowIfNull(value); _Title = value; } }

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
