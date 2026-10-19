namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>ApiError transport and domain data.</summary>
    public sealed class ApiError
    {
        /// <summary>Stable error code.</summary>
        public string Code { get; set; } = "";

        /// <summary>Safe explanation.</summary>
        public string Message { get; set; } = "";

        /// <summary>Correlation identifier.</summary>
        public string RequestId { get; set; } = "";

    }
}
