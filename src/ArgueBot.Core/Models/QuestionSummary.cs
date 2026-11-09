namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Open question counts for badges and notifications.</summary>
    public sealed class QuestionSummary
    {
        private string _NewestId = "";

        /// <summary>Open requests in conversations the caller created.</summary>
        public long OpenMine { get; set; }

        /// <summary>Open requests in the whole tenant.</summary>
        public long OpenTenant { get; set; }

        /// <summary>Runs in the tenant waiting for an answer.</summary>
        public long WaitingRuns { get; set; }

        /// <summary>Newest open request in the tenant, or empty.</summary>
        public string NewestId { get { return _NewestId; } set { ArgumentNullException.ThrowIfNull(value); _NewestId = value; } }
    }
}
