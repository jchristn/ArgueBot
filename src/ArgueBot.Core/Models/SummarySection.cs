namespace ArgueBot.Core.Models
{
    using System;

    /// <summary>One explicitly classified part of a model-written working memory.</summary>
    public sealed class SummarySection
    {
        private string _Kind = "";
        /// <summary>facts, contested_claims, positions, evidence, decisions, questions or constraints.</summary>
        public string Kind { get { return _Kind; } set { ArgumentNullException.ThrowIfNull(value); _Kind = value; } }
        private string _Content = "";
        /// <summary>Source-grounded text, explicitly stating when this category has no established information.</summary>
        public string Content { get { return _Content; } set { ArgumentNullException.ThrowIfNull(value); _Content = value; } }
    }
}
