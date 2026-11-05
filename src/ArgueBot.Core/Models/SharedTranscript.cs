namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>The read-only transcript behind a public link: the conversation, its debates, turns, proposals and votes. Secrets, prompts, runner settings, attempts and request history are never included.</summary>
    public sealed class SharedTranscript
    {
        private string _Title = "";
        /// <summary>Conversation title.</summary>
        public string Title { get { return _Title; } set { ArgumentNullException.ThrowIfNull(value); _Title = value; } }

        private string _OpeningQuestion = "";
        /// <summary>Opening question.</summary>
        public string OpeningQuestion { get { return _OpeningQuestion; } set { ArgumentNullException.ThrowIfNull(value); _OpeningQuestion = value; } }

        /// <summary>When the link stops working.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>Whether tool turns are included.</summary>
        public bool IncludeToolCalls { get; set; }

        private List<SharedRun> _Runs = new List<SharedRun>();
        /// <summary>Debates, oldest first.</summary>
        public List<SharedRun> Runs { get { return _Runs; } set { ArgumentNullException.ThrowIfNull(value); _Runs = value; } }

        private List<SharedMessage> _Messages = new List<SharedMessage>();
        /// <summary>Turns in order. Private directives are never included.</summary>
        public List<SharedMessage> Messages { get { return _Messages; } set { ArgumentNullException.ThrowIfNull(value); _Messages = value; } }

        private List<SharedProposal> _Proposals = new List<SharedProposal>();
        /// <summary>Proposals for protocol 2 debates.</summary>
        public List<SharedProposal> Proposals { get { return _Proposals; } set { ArgumentNullException.ThrowIfNull(value); _Proposals = value; } }
    }
}
