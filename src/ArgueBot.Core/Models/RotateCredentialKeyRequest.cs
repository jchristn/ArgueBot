namespace ArgueBot.Core.Models
{
    /// <summary>Compare-and-swap master key rotation request.</summary>
    public sealed class RotateCredentialKeyRequest
    {
        private string _ExpectedKeyId = "";
        /// <summary>Active key ID from the last status read; stale requests fail without another rotation.</summary>
        public string ExpectedKeyId { get { return _ExpectedKeyId; } set { System.ArgumentNullException.ThrowIfNull(value); _ExpectedKeyId = value; } }
    }
}
