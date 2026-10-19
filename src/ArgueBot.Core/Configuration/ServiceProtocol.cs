namespace ArgueBot.Core.Configuration
{
    /// <summary>Internal client/service compatibility, independent of product and database schema versions.</summary>
    public static class ServiceProtocol
    {
        /// <summary>Protocol 2 requires bounded message pages and explicit full-message reads. Older local clients must refuse attachment.</summary>
        public const int CurrentVersion = 2;
    }
}
