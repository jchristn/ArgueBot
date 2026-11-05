namespace ArgueBot.Core.Models
{
    /// <summary>Safe identity of the exact credential revision resolved for an invocation.</summary>
    public sealed class CredentialRevision
    {
        private string _CredentialId = "";
        /// <summary>Stable credential identity without its value.</summary>
        public string CredentialId { get { return _CredentialId; } set { System.ArgumentNullException.ThrowIfNull(value); _CredentialId = value; } }
        /// <summary>Revision read atomically with the encrypted value.</summary>
        public long Revision { get; set; }
    }
}
