namespace ArgueBot.Core.Models
{
    /// <summary>The session was revoked.</summary>
    public sealed class RevocationResponse
    {
        /// <summary>The session was revoked.</summary>
        public bool Revoked { get; set; } = true;
    }
}
