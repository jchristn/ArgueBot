namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Create or change a resource. On update, omitted (null) fields keep their current value, and revision must match the current revision.</summary>
    public sealed class ResourceRequest
    {
        /// <summary>Unique name within the tenant: 1 through 128 characters of letters, digits, hyphen, underscore, period and slash. It is stored in lowercase. Required on create; on update, a new value renames the resource.</summary>
        public string? Name { get; set; }

        /// <summary>Display title, up to 200 characters.</summary>
        public string? Title { get; set; }

        /// <summary>One-line summary, up to 500 characters.</summary>
        public string? Description { get; set; }

        /// <summary>Up to 16 tags, each 1 through 64 characters. Stored in lowercase without duplicates.</summary>
        public List<string>? Tags { get; set; }

        /// <summary>Markdown content, up to 262144 UTF-8 bytes.</summary>
        public string? Body { get; set; }

        /// <summary>Whether agents are prevented from changing the resource.</summary>
        public bool? Locked { get; set; }

        /// <summary>Expected current revision. Required on update; ignored on create.</summary>
        public long Revision { get; set; }
    }
}
