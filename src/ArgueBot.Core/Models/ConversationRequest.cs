namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>ConversationRequest transport and domain data.</summary>
    public sealed class ConversationRequest
    {
        private string _Title = "";

        /// <summary>Conversation title/topic.</summary>
        public string Title { get { return _Title; } set { ArgumentNullException.ThrowIfNull(value); _Title = value; } }

        /// <summary>Prompt chosen for each debate step; steps without an entry use the factory default. On update, null keeps the current choices.</summary>
        public Dictionary<string, string>? PromptIds { get; set; }

        /// <summary>The question a debate in this conversation starts from; blank means the title. On update, null keeps the stored question.</summary>
        public string? OpeningQuestion { get; set; }

        /// <summary>Pinned state.</summary>
        public bool Pinned { get; set; }

        /// <summary>Archived state.</summary>
        public bool Archived { get; set; }

        /// <summary>Expected revision.</summary>
        public long? Revision { get; set; }

    }
}
