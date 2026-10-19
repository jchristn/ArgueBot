namespace ArgueBot.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>A tracked, ordered schema upgrade with provider-owned statements.</summary>
    public sealed class SchemaMigration
    {
        private string _Description = "";
        private List<string> _Statements = new List<string>();

        /// <summary>Monotonic database schema version, independent of product version.</summary>
        public int Version { get; set; }
        /// <summary>Human-readable upgrade purpose.</summary>
        public string Description { get { return _Description; } set { ArgumentNullException.ThrowIfNull(value); _Description = value; } }
        /// <summary>Ordered parameterized DDL statements.</summary>
        public List<string> Statements { get { return _Statements; } set { ArgumentNullException.ThrowIfNull(value); _Statements = value; } }
    }
}
