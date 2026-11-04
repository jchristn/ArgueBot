namespace ArgueBot.Core.Services
{
    using ArgueBot.Core.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    internal static class SummaryContent
    {
        private static readonly Dictionary<string, string> _Labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "facts", "Agreed facts" },
            { "contested_claims", "Contested claims" },
            { "positions", "Participant positions" },
            { "evidence", "Evidence" },
            { "decisions", "Decisions" },
            { "questions", "Unanswered questions" },
            { "constraints", "Moderator constraints" }
        };
        internal static string Validate(OrchestrationResponse response, bool requireSections)
        {
            if (string.IsNullOrWhiteSpace(response.Summary)) throw Invalid();
            if (response.Sections.Count == 0 && !requireSections) return response.Summary;
            if (response.Sections.Count != _Labels.Count || response.Sections.Any(section => section == null || !_Labels.ContainsKey(section.Kind) || string.IsNullOrWhiteSpace(section.Content) || section.Content.Length > 4000) || response.Sections.Select(section => section.Kind).Distinct(StringComparer.Ordinal).Count() != _Labels.Count) throw Invalid();
            StringBuilder content = new StringBuilder(response.Summary);
            foreach (KeyValuePair<string, string> label in _Labels)
                content.Append("\n\n## ").Append(label.Value).Append("\n\n").Append(response.Sections.Single(section => section.Kind == label.Key).Content);
            if (content.Length > 16000) throw Invalid();
            return content.ToString();
        }
        private static DomainException Invalid()
        {
            return new DomainException(502, "summary_validation_failed", "Summary must include an overview and all seven distinct memory sections within the output budget. Original evidence and prior valid memory are preserved.");
        }
    }
}
