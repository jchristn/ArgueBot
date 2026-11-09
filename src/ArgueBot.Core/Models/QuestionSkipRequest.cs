namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Skip a whole request: the agent is told the answer is unavailable and continues on stated assumptions.</summary>
    public sealed class QuestionSkipRequest
    {
        private string _Note = "";
        private string _IdempotencyKey = "";

        /// <summary>Optional note passed to the agent, up to 2000 characters.</summary>
        public string Note { get { return _Note; } set { ArgumentNullException.ThrowIfNull(value); _Note = value; } }

        /// <summary>Client key, up to 128 characters, so a repeated skip returns the same result.</summary>
        public string IdempotencyKey { get { return _IdempotencyKey; } set { ArgumentNullException.ThrowIfNull(value); _IdempotencyKey = value; } }
    }
}
