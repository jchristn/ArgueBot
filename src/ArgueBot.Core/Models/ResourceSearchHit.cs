namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One resource matching a literal, case-insensitive search.</summary>
    public sealed class ResourceSearchHit
    {
        private string _Id = "";
        private string _Name = "";
        private string _Title = "";
        private string _Field = "";
        private string _Snippet = "";

        /// <summary>Resource ID (res_...).</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Resource name.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>Resource title.</summary>
        public string Title { get { return _Title; } set { ArgumentNullException.ThrowIfNull(value); _Title = value; } }

        /// <summary>First field that matched: name, title, description or body.</summary>
        public string Field { get { return _Field; } set { ArgumentNullException.ThrowIfNull(value); _Field = value; } }

        /// <summary>UTF-16 offset of the first body match, or -1 when the body did not match.</summary>
        public long MatchOffset { get; set; } = -1;

        /// <summary>Bounded text around the first match.</summary>
        public string Snippet { get { return _Snippet; } set { ArgumentNullException.ThrowIfNull(value); _Snippet = value; } }

        /// <summary>Current revision.</summary>
        public long Revision { get; set; }
    }
}
