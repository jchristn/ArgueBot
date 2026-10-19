namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Durable lifecycle of an administrator's recovery operation, without passphrases.</summary>
    public sealed class BackupOperation
    {
        private string _Id = "";
        private string _BackupId = "";
        private string _Kind = "";
        private string _State = "";
        private string _ErrorCode = "";
        private string _TargetProvider = "";
        private string _TargetConnectionEnvironmentVariable = "";
        private string _RetainedStagingHome = "";
        private string _TargetHome = "";
        private List<string> _MissingReferences = new List<string>();

        /// <summary>Opaque operation identifier.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }
        /// <summary>Associated artifact.</summary>
        public string BackupId { get { return _BackupId; } set { ArgumentNullException.ThrowIfNull(value); _BackupId = value; } }
        /// <summary>Create, verify or restore.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }
        /// <summary>Queued, running, completed or failed.</summary>
        public string State { get { return _State; } set { ArgumentNullException.ThrowIfNull(value); _State = value; } }
        /// <summary>Safe failure code, without exception details or credentials.</summary>
        public string ErrorCode { get { return _ErrorCode; } set { ArgumentNullException.ThrowIfNull(value); _ErrorCode = value; } }
        /// <summary>Explicit new-home restore destination, otherwise empty.</summary>
        public string TargetHome { get { return _TargetHome; } set { ArgumentNullException.ThrowIfNull(value); _TargetHome = value; } }
        /// <summary>Restore destination provider, otherwise empty.</summary>
        public string TargetProvider { get { return _TargetProvider; } set { ArgumentNullException.ThrowIfNull(value); _TargetProvider = value; } }
        /// <summary>Nonsecret PostgreSQL destination environment reference, otherwise empty.</summary>
        public string TargetConnectionEnvironmentVariable { get { return _TargetConnectionEnvironmentVariable; } set { ArgumentNullException.ThrowIfNull(value); _TargetConnectionEnvironmentVariable = value; } }
        /// <summary>Protected staging home retained after an externally committed database could not be reconciled or published. Requires explicit operator recovery.</summary>
        public string RetainedStagingHome { get { return _RetainedStagingHome; } set { ArgumentNullException.ThrowIfNull(value); _RetainedStagingHome = value; } }
        /// <summary>Requested listener port, or the effective port after successful restoration. Zero initially preserves the archive setting.</summary>
        public int Port { get; set; }
        /// <summary>Credential names missing during restore.</summary>
        public List<string> MissingReferences { get { return _MissingReferences; } set { ArgumentNullException.ThrowIfNull(value); _MissingReferences = value; } }
        /// <summary>UTC acceptance time.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>UTC last status change.</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
