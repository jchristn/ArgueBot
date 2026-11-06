namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One markdown file to import. Optional front matter between --- lines supplies name, title, description, tags and locked.</summary>
    public sealed class ResourceImportFile
    {
        private string _FileName = "";
        private string _Content = "";

        /// <summary>Original file name. Without a name in the front matter, the name comes from the file name without its .md extension.</summary>
        public string FileName { get { return _FileName; } set { ArgumentNullException.ThrowIfNull(value); _FileName = value; } }

        /// <summary>File text, front matter included.</summary>
        public string Content { get { return _Content; } set { ArgumentNullException.ThrowIfNull(value); _Content = value; } }
    }
}
