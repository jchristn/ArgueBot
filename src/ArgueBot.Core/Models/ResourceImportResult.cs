namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Resources an import created or replaced.</summary>
    public sealed class ResourceImportResult
    {
        private List<ResourceSummary> _Items = new List<ResourceSummary>();

        /// <summary>Imported resources in file order.</summary>
        public List<ResourceSummary> Items { get { return _Items; } set { ArgumentNullException.ThrowIfNull(value); _Items = value; } }

        /// <summary>Number of new resources.</summary>
        public int Created { get; set; }

        /// <summary>Number of existing resources replaced.</summary>
        public int Updated { get; set; }
    }
}
