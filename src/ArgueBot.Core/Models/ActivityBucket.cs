namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>ActivityBucket operational contract.</summary>
    public sealed class ActivityBucket
    {
        /// <summary>UTC bucket boundary.</summary>
        public DateTime StartUtc { get; set; }
        /// <summary>Nonerror requests in bucket.</summary>
        public long Success { get; set; }
        /// <summary>Failed requests in bucket.</summary>
        public long Failure { get; set; }
    }
}
