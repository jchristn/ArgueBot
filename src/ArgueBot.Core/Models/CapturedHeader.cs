namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>CapturedHeader operational contract.</summary>
    public sealed class CapturedHeader
    {
        private string _Name = "";
        private string _Value = "";

        /// <summary>Header name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }
        /// <summary>Redacted header value.</summary>
        public string Value { get { return _Value; } set { ArgumentNullException.ThrowIfNull(value); _Value = value; } }
        /// <summary>Whether this is a response header.</summary>
        public bool Response { get; set; }
    }
}
