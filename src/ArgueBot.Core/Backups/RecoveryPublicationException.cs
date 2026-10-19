namespace ArgueBot.Core.Backups
{
    using System;
    using System.IO;

    /// <summary>A PostgreSQL destination was restored, but the new home could not be published. The protected staging home is retained for explicit operator recovery.</summary>
    public sealed class RecoveryPublicationException : IOException
    {
        /// <summary>Retained owner-readable staging home. Contains configuration and recovery provenance; never delete it automatically as artifact cleanup.</summary>
        public string StagingHome { get; }
        /// <summary>Create a typed failure without including credentials or the inner exception text in its public message.</summary>
        public RecoveryPublicationException(string stagingHome, Exception inner) : base("The destination database was restored, but publishing its home failed. Its protected staging home was retained for operator recovery.", inner)
        { ArgumentNullException.ThrowIfNull(stagingHome); StagingHome = stagingHome; }
    }
}
