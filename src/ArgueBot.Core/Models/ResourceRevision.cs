namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A saved copy of a resource after one change. The service keeps the latest 100 revisions of each resource.</summary>
    public sealed class ResourceRevision
    {
        private string _Id = "";
        private string _ResourceId = "";
        private string _Name = "";
        private string _Title = "";
        private string _Description = "";
        private List<string> _Tags = new List<string>();
        private string _Body = "";
        private string _Change = "";
        private string _AuthorKind = "user";
        private string _AuthorId = "";
        private string _AuthorName = "";
        private string _RunId = "";
        private string _AttemptId = "";

        /// <summary>Revision record identifier (rsr_...).</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Resource this revision belongs to (res_...).</summary>
        public string ResourceId { get { return _ResourceId; } set { ArgumentNullException.ThrowIfNull(value); _ResourceId = value; } }

        /// <summary>Revision number the change produced.</summary>
        public long Revision { get; set; }

        /// <summary>Resource name at this revision.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        /// <summary>Title at this revision.</summary>
        public string Title { get { return _Title; } set { ArgumentNullException.ThrowIfNull(value); _Title = value; } }

        /// <summary>Description at this revision.</summary>
        public string Description { get { return _Description; } set { ArgumentNullException.ThrowIfNull(value); _Description = value; } }

        /// <summary>Tags at this revision.</summary>
        public List<string> Tags { get { return _Tags; } set { ArgumentNullException.ThrowIfNull(value); _Tags = value; } }

        /// <summary>Full markdown body at this revision. Empty in revision lists; read one revision to get it.</summary>
        public string Body { get { return _Body; } set { ArgumentNullException.ThrowIfNull(value); _Body = value; } }

        /// <summary>Body length in UTF-16 characters.</summary>
        public long Characters { get; set; }

        /// <summary>Whether the resource was locked at this revision.</summary>
        public bool Locked { get; set; }

        /// <summary>What changed: create, update, rename, lock, unlock, append, revert or import.</summary>
        public string Change { get { return _Change; } set { ArgumentNullException.ThrowIfNull(value); _Change = value; } }

        /// <summary>user or agent.</summary>
        public string AuthorKind { get { return _AuthorKind; } set { ArgumentNullException.ThrowIfNull(value); _AuthorKind = value; } }

        /// <summary>User ID (usr_...) or agent seat ID (par_...) that made the change.</summary>
        public string AuthorId { get { return _AuthorId; } set { ArgumentNullException.ThrowIfNull(value); _AuthorId = value; } }

        /// <summary>Display name of the author.</summary>
        public string AuthorName { get { return _AuthorName; } set { ArgumentNullException.ThrowIfNull(value); _AuthorName = value; } }

        /// <summary>Debate run whose agent made the change (run_...), empty for user changes.</summary>
        public string RunId { get { return _RunId; } set { ArgumentNullException.ThrowIfNull(value); _RunId = value; } }

        /// <summary>Generation attempt that made the change, empty for user changes.</summary>
        public string AttemptId { get { return _AttemptId; } set { ArgumentNullException.ThrowIfNull(value); _AttemptId = value; } }

        /// <summary>Time of the change in UTC.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
