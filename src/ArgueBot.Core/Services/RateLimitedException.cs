namespace ArgueBot.Core.Services
{
    /// <summary>429 rate_limited, with the number of seconds the caller should wait.</summary>
    public sealed class RateLimitedException : DomainException
    {
        /// <summary>Create the exception.</summary>
        public RateLimitedException(int retryAfterSeconds) : base(429, "rate_limited", "Too many failed sign-in attempts. Try again in " + retryAfterSeconds + " seconds.") { RetryAfterSeconds = retryAfterSeconds; }
        /// <summary>Seconds to wait before trying again.</summary>
        public int RetryAfterSeconds { get; }
    }
}
