namespace ArgueBot.Server
{
    /// <summary>Session secret returned only by successful enrollment.</summary>
    public sealed class PairResponse
    {
        /// <summary>Opaque twelve-hour bearer session token.</summary>
        public string Token { get; set; } = "";
    }
}
