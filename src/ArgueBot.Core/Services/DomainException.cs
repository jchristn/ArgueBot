namespace ArgueBot.Core.Services
{
    using System;

    /// <summary>A safe domain failure mapped to a status and stable code.</summary>
    public sealed class DomainException : InvalidOperationException
    {
        /// <summary>HTTP status.</summary>
        public int StatusCode { get; }
        /// <summary>Stable failure identifier.</summary>
        public string Code { get; }
        /// <summary>Create a domain error with safe context.</summary>
        public DomainException(int statusCode, string code, string message) : base(message) { StatusCode = statusCode; Code = code; }
    }
}
