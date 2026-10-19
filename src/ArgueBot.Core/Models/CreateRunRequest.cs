namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>CreateRunRequest transport and domain data.</summary>
    public sealed class CreateRunRequest
    {
        /// <summary>Ordered profiles.</summary>
        public List<string> ParticipantIds { get; set; } = new List<string>();

        /// <summary>Round limit.</summary>
        public int MaxRounds { get; set; } = 5;

        /// <summary>Intervention countdown.</summary>
        public int InterventionSeconds { get; set; } = 10;

        /// <summary>Unique command key.</summary>
        public string IdempotencyKey { get; set; } = "";

    }
}
