namespace ArgueBot.Core.Models
{
    using System;
    /// <summary>Minimal anonymous readiness information, without identity or private configuration.</summary>
    public sealed class HealthResponse
    {
        private string _Status = "ready";

        /// <summary>Readiness state.</summary>
        public string Status { get { return _Status; } set { ArgumentNullException.ThrowIfNull(value); _Status = value; } }
        /// <summary>Current database schema version, independent of product version.</summary>
        public int Schema { get; set; } = 3;
    }
}
