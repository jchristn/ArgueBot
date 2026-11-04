namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Explicit connection check result; listing models does not verify generation or tool support.</summary>
    public sealed class EndpointDiscovery
    {
        /// <summary>Checked endpoint identifier.</summary>
        public string EndpointId { get; set; } = "";
        /// <summary>Configuration revision checked.</summary>
        public long Revision { get; set; }
        /// <summary>Time the endpoint successfully returned its model list.</summary>
        public DateTime CheckedUtc { get; set; }
        /// <summary>Distinct advertised model IDs, at most 1000; manual IDs remain supported.</summary>
        public List<string> Models { get; set; } = new List<string>();
        /// <summary>Whether more advertised IDs were omitted from this bounded result.</summary>
        public bool Truncated { get; set; }
    }
}
