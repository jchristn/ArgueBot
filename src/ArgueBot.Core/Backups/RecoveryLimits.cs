namespace ArgueBot.Core.Backups
{
    /// <summary>Explicit archive resource limits, enforced before extracting user data.</summary>
    public sealed class RecoveryLimits
    {
        /// <summary>Maximum combined uncompressed bytes, default ten GiB.</summary>
        public long MaximumBytes { get; set; } = 10737418240;
        /// <summary>Maximum archive entries, default 4096.</summary>
        public int MaximumFiles { get; set; } = 4096;
    }
}
