namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Change a password.</summary>
    public sealed class PasswordChangeRequest
    {
        private string _CurrentPassword = "";
        /// <summary>The current password; required when changing your own password.</summary>
        public string CurrentPassword { get { return _CurrentPassword; } set { ArgumentNullException.ThrowIfNull(value); _CurrentPassword = value; } }

        private string _NewPassword = "";
        /// <summary>The new password, 8 through 256 characters.</summary>
        public string NewPassword { get { return _NewPassword; } set { ArgumentNullException.ThrowIfNull(value); _NewPassword = value; } }
    }
}
