namespace ArgueBot.Core.Models
{
    /// <summary>The host is draining and reloading configuration.</summary>
    public sealed class RestartResponse
    {
        /// <summary>The host is draining and reloading configuration.</summary>
        public bool Restarting { get; set; } = true;
    }
}
