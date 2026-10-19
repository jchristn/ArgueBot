namespace ArgueBot.Core.Models
{
    /// <summary>One-time session token returned only by successful authentication exchange.</summary>
    public sealed class SessionResponse
    {
        /// <summary>Opaque revocable one-hour session. Keep it out of logs, URLs and source control.</summary>
        public string Token { get; set; } = "";
    }
}
