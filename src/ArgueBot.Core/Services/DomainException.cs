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
        /// <summary>Request field the failure applies to, in camel case, or empty when it is not field-specific.</summary>
        public string Field { get; } = "";
        /// <summary>Create a domain error with safe context.</summary>
        public DomainException(int statusCode, string code, string message) : base(message) { StatusCode = statusCode; Code = code; }
        /// <summary>Create a domain error that names the request field to correct.</summary>
        public DomainException(int statusCode, string code, string message, string field) : base(message) { ArgumentNullException.ThrowIfNull(field); StatusCode = statusCode; Code = code; Field = field; }
    }
}
