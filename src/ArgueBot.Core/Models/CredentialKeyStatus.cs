namespace ArgueBot.Core.Models
{
    /// <summary>Safe keyring status, without key bytes or ciphertext.</summary>
    public sealed class CredentialKeyStatus
    {
        private string _ActiveKeyId = "";
        /// <summary>Active master key identity; use as the expected identity for rotation.</summary>
        public string ActiveKeyId { get { return _ActiveKeyId; } set { System.ArgumentNullException.ThrowIfNull(value); _ActiveKeyId = value; } }
        /// <summary>Number of keys retained to read existing records and older backups.</summary>
        public int RetainedKeyCount { get; set; }
        /// <summary>Credential records reencrypted by this operation; zero on a status read.</summary>
        public long ReencryptedCredentials { get; set; }
        /// <summary>Disabled credentials that could not be decrypted and were left unchanged by this rotation; replace or ignore them.</summary>
        public long SkippedCredentials { get; set; }
        /// <summary>Earlier keys removed by this operation; zero unless keys were retired.</summary>
        public int RetiredKeyCount { get; set; }
    }
}
