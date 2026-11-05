namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Create or update a user. Password is required when creating and ignored when updating; use the password route instead.</summary>
    public sealed class UserRequest
    {
        private string _FirstName = "";
        /// <summary>First name.</summary>
        public string FirstName { get { return _FirstName; } set { ArgumentNullException.ThrowIfNull(value); _FirstName = value; } }

        private string _LastName = "";
        /// <summary>Last name.</summary>
        public string LastName { get { return _LastName; } set { ArgumentNullException.ThrowIfNull(value); _LastName = value; } }

        private string _Email = "";
        /// <summary>Email address, unique within the tenant.</summary>
        public string Email { get { return _Email; } set { ArgumentNullException.ThrowIfNull(value); _Email = value; } }

        private string _Password = "";
        /// <summary>Initial password, 8 through 256 characters, when creating.</summary>
        public string Password { get { return _Password; } set { ArgumentNullException.ThrowIfNull(value); _Password = value; } }

        /// <summary>System administrator flag; only system administrators may set it.</summary>
        public bool IsAdmin { get; set; }

        /// <summary>Tenant administrator flag.</summary>
        public bool IsTenantAdmin { get; set; }

        /// <summary>False disables the user.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Expected revision when updating.</summary>
        public long Revision { get; set; }
    }
}
