namespace ArgueBot.Core.Backups
{
    using System;
    using System.Collections.Generic;

    /// <summary>Declared JSONL record for logical relational backups, never a runtime domain storage blob.</summary>
    public sealed class BackupRecord
    {
        private string _Kind = "";
        private string _Provider = "";
        private string _Table = "";
        private List<string> _Columns = new List<string>();
        private List<string?> _Values = new List<string?>();
        private string _Digest = "";

        /// <summary>Record discriminator: header, table, row or footer.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }
        /// <summary>Backup interchange format version.</summary>
        public int FormatVersion { get; set; } = 1;
        /// <summary>Database schema at capture time.</summary>
        public int SchemaVersion { get; set; }
        /// <summary>Source driver for provenance only; restores use target-specific commands.</summary>
        public string Provider { get { return _Provider; } set { ArgumentNullException.ThrowIfNull(value); _Provider = value; } }
        /// <summary>UTC capture time.</summary>
        public DateTime CreatedUtc { get; set; }
        /// <summary>Known relational table name.</summary>
        public string Table { get { return _Table; } set { ArgumentNullException.ThrowIfNull(value); _Table = value; } }
        /// <summary>Ordered known column names for a table record.</summary>
        public List<string> Columns { get { return _Columns; } set { ArgumentNullException.ThrowIfNull(value); _Columns = value; } }
        /// <summary>Ordered invariant text/null cell values for a row record.</summary>
        public List<string?> Values { get { return _Values; } set { ArgumentNullException.ThrowIfNull(value); _Values = value; } }
        /// <summary>Footer total across all table rows.</summary>
        public long TotalRows { get; set; }
        /// <summary>SHA-256 of every preceding UTF-8 record including its LF delimiter.</summary>
        public string Digest { get { return _Digest; } set { ArgumentNullException.ThrowIfNull(value); _Digest = value; } }
    }
}
