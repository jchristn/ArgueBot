namespace ArgueBot.Core.Models
{
    /// <summary>Optional HTTP generation overrides; null preserves the transport adapter default.</summary>
    public sealed class ModelGenerationSettings
    {
        /// <summary>Requested generated-token ceiling, 1 through 1048576, independently enforced by the provider. Null retains the adapter default.</summary>
        public int? MaxOutputTokens { get; set; }
        /// <summary>Sampling temperature from zero through two, or null to omit the override.</summary>
        public double? Temperature { get; set; }
        /// <summary>Nucleus sampling probability from zero through one, or null to omit the override.</summary>
        public double? TopP { get; set; }
    }
}
