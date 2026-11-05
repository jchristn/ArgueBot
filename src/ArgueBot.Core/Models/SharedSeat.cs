namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A debate participant as a public reader sees it: its ID and name only.</summary>
    public sealed class SharedSeat
    {
        private string _Id = "";
        /// <summary>Seat ID.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Display name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }
    }
}
