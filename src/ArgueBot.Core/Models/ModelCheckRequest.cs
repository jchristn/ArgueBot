namespace ArgueBot.Core.Models
{
    /// <summary>Explicit, potentially billable model check; never invoked by saving configuration.</summary>
    public sealed class ModelCheckRequest
    {
        private string _IdempotencyKey = "";
        /// <summary>Stable command identity. Reusing it returns the recorded check without invoking again.</summary>
        public string IdempotencyKey { get { return _IdempotencyKey; } set { System.ArgumentNullException.ThrowIfNull(value); _IdempotencyKey = value; } }
        /// <summary>Ask the model to perform one harmless advertised tool round trip.</summary>
        public bool CheckTools { get; set; }
    }
}
