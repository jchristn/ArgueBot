namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One option in an ask_user question, as the agent writes it.</summary>
    public sealed class AskUserOptionArgument
    {
        private string _Id = "";
        private string _Label = "";
        private string _Description = "";

        /// <summary>Option identifier, 1 through 32 characters, unique within the question.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        /// <summary>Option text, up to 80 characters.</summary>
        public string Label { get { return _Label; } set { ArgumentNullException.ThrowIfNull(value); _Label = value; } }

        /// <summary>Optional explanation, up to 300 characters.</summary>
        public string Description { get { return _Description; } set { ArgumentNullException.ThrowIfNull(value); _Description = value; } }

        /// <summary>Whether the agent recommends this option.</summary>
        public bool Recommended { get; set; }
    }
}
