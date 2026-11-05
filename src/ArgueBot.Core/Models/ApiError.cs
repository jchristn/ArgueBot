namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>ApiError transport and domain data.</summary>
    public sealed class ApiError
    {
        private string _Code = "";
        private string _Message = "";
        private string _RequestId = "";
        private string _Field = "";

        /// <summary>Stable error code.</summary>
        public string Code { get { return _Code; } set { ArgumentNullException.ThrowIfNull(value); _Code = value; } }

        /// <summary>Safe explanation.</summary>
        public string Message { get { return _Message; } set { ArgumentNullException.ThrowIfNull(value); _Message = value; } }

        /// <summary>Correlation identifier.</summary>
        public string RequestId { get { return _RequestId; } set { ArgumentNullException.ThrowIfNull(value); _RequestId = value; } }

        /// <summary>Request field to correct, in camel case, or empty when the failure is not field-specific.</summary>
        public string Field { get { return _Field; } set { ArgumentNullException.ThrowIfNull(value); _Field = value; } }

    }
}
