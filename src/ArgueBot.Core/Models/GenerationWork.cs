namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>Atomically accepted answer and its generation-attempt record.</summary>
    public sealed class GenerationWork
    {
        private Message _Answer = new Message();
        private GenerationAttempt _Attempt = new GenerationAttempt();
        /// <summary>New streaming answer whose partial content is checkpointed before completion.</summary>
        public Message Answer { get { return _Answer; } set { ArgumentNullException.ThrowIfNull(value); _Answer = value; } }
        /// <summary>Pending external operation provenance.</summary>
        public GenerationAttempt Attempt { get { return _Attempt; } set { ArgumentNullException.ThrowIfNull(value); _Attempt = value; } }
    }
}
