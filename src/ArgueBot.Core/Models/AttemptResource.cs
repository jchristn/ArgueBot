namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A resource an agent read or wrote during one generation attempt, with the revision involved.</summary>
    public sealed class AttemptResource
    {
        private string _ResourceId = "";
        private string _Name = "";
        private string _Access = "read";
        private string _Tool = "";

        /// <summary>Resource ID (res_...).</summary>
        public string ResourceId { get { return _ResourceId; } set { ArgumentNullException.ThrowIfNull(value); _ResourceId = value; } }

        /// <summary>Resource name at the time of the call.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>Revision read, or the revision a write produced.</summary>
        public long Revision { get; set; }

        /// <summary>read or write.</summary>
        public string Access { get { return _Access; } set { ArgumentNullException.ThrowIfNull(value); _Access = value; } }

        /// <summary>Tool used: read_resource, create_resource, update_resource, edit_resource or append_resource.</summary>
        public string Tool { get { return _Tool; } set { ArgumentNullException.ThrowIfNull(value); _Tool = value; } }

        /// <summary>UTF-8 bytes written; zero for reads.</summary>
        public long Bytes { get; set; }

        /// <summary>Time of the call in UTC.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
