namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>A new public link. The token and URL are returned only this once.</summary>
    public sealed class ConversationShareCreated
    {
        private ConversationShare _Share = new ConversationShare();
        /// <summary>The stored link.</summary>
        public ConversationShare Share { get { return _Share; } set { ArgumentNullException.ThrowIfNull(value); _Share = value; } }

        private string _Token = "";
        /// <summary>Link token. Anyone with it can read the transcript until it expires or is revoked.</summary>
        public string Token { get { return _Token; } set { ArgumentNullException.ThrowIfNull(value); _Token = value; } }

        private string _Path = "";
        /// <summary>Dashboard path for the link, /share/{token}.</summary>
        public string Path { get { return _Path; } set { ArgumentNullException.ThrowIfNull(value); _Path = value; } }

        private string _Url = "";
        /// <summary>Absolute link built from the host this request used.</summary>
        public string Url { get { return _Url; } set { ArgumentNullException.ThrowIfNull(value); _Url = value; } }
    }
}
