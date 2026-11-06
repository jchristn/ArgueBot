namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A resource without its body, for lists.</summary>
    public sealed class ResourceSummary
    {
        private string _Id = "";
        private string _Name = "";
        private string _Title = "";
        private string _Description = "";
        private List<string> _Tags = new List<string>();
        private string _CreatedByUserId = "";
        private string _UpdatedByKind = "user";
        private string _UpdatedById = "";
        private string _UpdatedByName = "";

        /// <summary>Stable resource identifier (res_...). Names can change; the ID does not.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Unique name within the tenant, 1 through 128 characters of lowercase letters, digits, hyphen, underscore, period and slash, for example billing or team/conventions. Agents pass it to read_resource.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>Display title, up to 200 characters. Empty uses the name.</summary>
        public string Title { get { return _Title; } set { ArgumentNullException.ThrowIfNull(value); _Title = value; } }

        /// <summary>One-line summary, up to 500 characters, shown by list_resources so an agent can decide what to read.</summary>
        public string Description { get { return _Description; } set { ArgumentNullException.ThrowIfNull(value); _Description = value; } }

        /// <summary>Up to 16 lowercase tags, each 1 through 64 characters of letters, digits, hyphen, underscore, period and slash.</summary>
        public List<string> Tags { get { return _Tags; } set { ArgumentNullException.ThrowIfNull(value); _Tags = value; } }

        /// <summary>Body length in UTF-16 characters, the unit read_resource offsets use.</summary>
        public long Characters { get; set; }

        /// <summary>Body size in UTF-8 bytes.</summary>
        public long Bytes { get; set; }

        /// <summary>When true, agents can read but never change this resource. Users can still edit it.</summary>
        public bool Locked { get; set; }

        /// <summary>Optimistic revision, starting at 1 and increasing with every change. Updates, deletes and reverts must supply the current value.</summary>
        public long Revision { get; set; }

        /// <summary>User who created the resource, or the user whose debate created it.</summary>
        public string CreatedByUserId { get { return _CreatedByUserId; } set { ArgumentNullException.ThrowIfNull(value); _CreatedByUserId = value; } }

        /// <summary>Who made the latest change: user or agent. Agent-written text persists into later debates, so treat it as unreviewed until a user checks it.</summary>
        public string UpdatedByKind { get { return _UpdatedByKind; } set { ArgumentNullException.ThrowIfNull(value); _UpdatedByKind = value; } }

        /// <summary>User ID (usr_...) or agent seat ID (par_...) that made the latest change.</summary>
        public string UpdatedById { get { return _UpdatedById; } set { ArgumentNullException.ThrowIfNull(value); _UpdatedById = value; } }

        /// <summary>Display name of the user or agent that made the latest change.</summary>
        public string UpdatedByName { get { return _UpdatedByName; } set { ArgumentNullException.ThrowIfNull(value); _UpdatedByName = value; } }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Last change time in UTC.</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
