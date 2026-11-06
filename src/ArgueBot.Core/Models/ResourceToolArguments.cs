namespace ArgueBot.Core.Models
{
    using System.Collections.Generic;

    /// <summary>Arguments accepted by the resource tools. Each tool reads only the fields its schema lists.</summary>
    public sealed class ResourceToolArguments
    {
        /// <summary>Resource name, for example billing or team/conventions.</summary>
        public string Name { get; set; } = "";

        /// <summary>list_resources: only resources with this tag.</summary>
        public string Tag { get; set; } = "";

        /// <summary>list_resources: exclusive name cursor from nextAfter.</summary>
        public string After { get; set; } = "";

        /// <summary>Most results: 1 through 50 for list_resources (default 20), 1 through 20 for search_resources (default 10).</summary>
        public int? Limit { get; set; }

        /// <summary>search_resources: literal text to find, 1 through 4000 characters.</summary>
        public string Query { get; set; } = "";

        /// <summary>read_resource: UTF-16 offset to start reading from.</summary>
        public int Offset { get; set; }

        /// <summary>read_resource: characters to read, 1 through 32768. Default 8192.</summary>
        public int Length { get; set; } = 8192;

        /// <summary>create_resource and update_resource: title.</summary>
        public string? Title { get; set; }

        /// <summary>create_resource and update_resource: one-line description.</summary>
        public string? Description { get; set; }

        /// <summary>create_resource: tags.</summary>
        public List<string>? Tags { get; set; }

        /// <summary>create_resource and update_resource: full markdown body.</summary>
        public string? Body { get; set; }

        /// <summary>update_resource and edit_resource: the revision the change is based on.</summary>
        public long? Revision { get; set; }

        /// <summary>edit_resource: exact text to replace; it must occur exactly once.</summary>
        public string? Find { get; set; }

        /// <summary>edit_resource: replacement text.</summary>
        public string? Replace { get; set; }

        /// <summary>append_resource: text to add at the end.</summary>
        public string? Text { get; set; }
    }
}
