namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A placeholder a prompt can use, with the debate steps where it has a value.</summary>
    public sealed class PromptParameter
    {
        private string _Name = "";
        /// <summary>Placeholder name, written as {name} in a prompt.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Description = "";
        /// <summary>What the placeholder is replaced with.</summary>
        public string Description { get { return _Description; } set { ArgumentNullException.ThrowIfNull(value); _Description = value; } }

        private List<string> _Purposes = new List<string>();
        /// <summary>Debate steps where the placeholder may be used.</summary>
        public List<string> Purposes { get { return _Purposes; } set { ArgumentNullException.ThrowIfNull(value); _Purposes = value; } }

        private List<string> _RequiredFor = new List<string>();
        /// <summary>Debate steps whose prompts must include the placeholder.</summary>
        public List<string> RequiredFor { get { return _RequiredFor; } set { ArgumentNullException.ThrowIfNull(value); _RequiredFor = value; } }
    }
}
