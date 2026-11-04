namespace ArgueBot.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A reusable debate prompt for one protocol step. Factory prompts are read-only and can be duplicated.</summary>
    public sealed class PromptTemplate
    {
        private string _Id = "";
        /// <summary>Prompt identifier. Factory prompts use stable pmt_factory_ identifiers.</summary>
        public string Id { get { return _Id; } set { ArgumentNullException.ThrowIfNull(value); _Id = value; } }

        private string _Name = "";
        /// <summary>Display name, 1 through 200 characters.</summary>
        public string Name { get { return _Name; } set { ArgumentNullException.ThrowIfNull(value); _Name = value; } }

        private string _Purpose = "";
        /// <summary>The debate step this prompt drives: question, response, synthesis, ratification or compaction. Fixed after creation.</summary>
        public string Purpose { get { return _Purpose; } set { ArgumentNullException.ThrowIfNull(value); _Purpose = value; } }

        private string _Description = "";
        /// <summary>Optional note about when to use this prompt, up to 1000 characters.</summary>
        public string Description { get { return _Description; } set { ArgumentNullException.ThrowIfNull(value); _Description = value; } }

        private string _Body = "";
        /// <summary>Prompt text with {parameter} placeholders. Use {{ and }} for literal braces. Up to 32000 characters.</summary>
        public string Body { get { return _Body; } set { ArgumentNullException.ThrowIfNull(value); _Body = value; } }

        /// <summary>True for a factory default, which cannot be edited or deleted.</summary>
        public bool Factory { get; set; }

        /// <summary>Optimistic revision. Updates and deletes must supply the current value.</summary>
        public long Revision { get; set; }

        /// <summary>Creation time in UTC.</summary>
        public DateTime CreatedUtc { get; set; }

        /// <summary>Last change time in UTC.</summary>
        public DateTime UpdatedUtc { get; set; }
    }
}
