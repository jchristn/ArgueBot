namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Import 1 through 50 markdown files as resources in one transaction. Any invalid file rejects the whole import.</summary>
    public sealed class ResourceImportRequest
    {
        private List<ResourceImportFile> _Files = new List<ResourceImportFile>();

        /// <summary>Files to import.</summary>
        public List<ResourceImportFile> Files { get { return _Files; } set { ArgumentNullException.ThrowIfNull(value); _Files = value; } }

        /// <summary>When true, a file whose name already exists replaces that resource's content as a new revision. When false (the default), an existing name fails the import with resource_exists.</summary>
        public bool Overwrite { get; set; }
    }
}
