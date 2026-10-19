namespace ArgueBot.Core.Configuration
{
    /// <summary>Internal client/service compatibility, independent of product and database schema versions.</summary>
    public static class ServiceProtocol
    {
        /// <summary>Protocol 2 requires bounded message pages and explicit full-message reads. Older local clients must refuse attachment.</summary>
        public const int CurrentVersion = 2;
        /// <summary>Maximum normalized SSE frame size in UTF-16 code units, including protocol fields. Generated browser metadata uses the same value.</summary>
        public const int MaximumEventFrameCharacters = 1048576;
        /// <summary>Native event-read idle deadline in seconds, longer than the service heartbeat interval.</summary>
        public const int EventIdleSeconds = 15;
        /// <summary>Service SSE heartbeat interval in seconds.</summary>
        public const int EventHeartbeatSeconds = 5;
        /// <summary>Maximum normal SSE connection lifetime in seconds before reconnecting with the last committed cursor.</summary>
        public const int EventConnectionSeconds = 30;
    }
}
